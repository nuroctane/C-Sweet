using CSweet.Agent.SDK;
using CSweet.Application.Agents;
using CSweet.Application.Security;
using CSweet.Contracts.Agents;
using CSweet.Infrastructure.Setup;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CSweet.AgentHost.Broker;

public interface IPlatformCapabilityDispatcher
{
    IAsyncEnumerable<CapabilityResult> InvokeAsync(
        AgentSession session,
        RequestCapability request,
        CancellationToken cancellationToken);
}

public sealed class PlatformCapabilityDispatcher(
    IEnumerable<IPlatformCapabilityHandler> handlers,
    PluginSetupAssistancePolicy? setupPolicy = null,
    ProjectCapabilityPolicy? projectPolicy = null,PluginAccountDataExposureService? providerData = null,
    IAgentPostureReader? postures = null,
    IAgentCapabilityApprovalService? approvals = null,
    IAgentPolicyService? temporalPolicy = null) : IPlatformCapabilityDispatcher
{
    private readonly IReadOnlyList<IPlatformCapabilityHandler> _handlers = handlers.ToList();

    public async IAsyncEnumerable<CapabilityResult> InvokeAsync(
        AgentSession session,
        RequestCapability request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? denial = null;
        if (setupPolicy is not null && !McpToolCatalog.SelfReadCapabilities.Contains(request.Capability))
        {
            try
            {
                if (!Guid.TryParse(session.InstallationId, out var installationId))
                    throw new UnauthorizedAccessException("The installation identity is invalid.");
                using var document = JsonDocument.Parse(request.Payload.IsEmpty ? "{}" : System.Text.Encoding.UTF8.GetString(request.Payload.Span));
                await setupPolicy.ValidateCapabilityAsync(session.BusinessId, installationId,
                    request.Capability, document.RootElement, cancellationToken);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or JsonException or InvalidOperationException)
            { denial = "The installation may not invoke this capability in its current setup context."; }
        }
        if (denial is null && projectPolicy is not null)
        {
            try
            {
                if (!Guid.TryParse(session.BusinessId, out var org) || !Guid.TryParse(session.InstallationId, out var agent)) throw new UnauthorizedAccessException("Invalid agent identity.");
                await projectPolicy.ValidateAsync(org, agent, request.Capability, request.Payload.ToElement(), cancellationToken);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or InvalidOperationException or JsonException) { denial = e.Message; }
        }
        if (denial is not null)
        {
            yield return Failure(request.RequestId, denial);
            yield break;
        }
        var requested = session.Grant.RequestedCapabilities ?? new HashSet<string>(StringComparer.Ordinal);
        if (!IsImplicitPlatformCapability(request.Capability) && !requested.Contains(request.Capability))
        {
            yield return Failure(request.RequestId,
                $"Agent '{session.AgentId}' may not request '{request.Capability}'.");
            yield break;
        }

        var handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(request.Capability));
        if (handler is null)
        { yield return Failure(request.RequestId, $"No platform handler provides '{request.Capability}'."); yield break; }

        CapabilityAdmission? admission = null;
        var risk = CapabilityRiskCatalog.Classify(request.Capability);
        if (postures is not null)
        {
            if (!Guid.TryParse(session.InstallationId, out var installation))
            { yield return Failure(request.RequestId, "The installation identity is invalid."); yield break; }
            var assignment = await postures.GetAsync(installation, cancellationToken);
            if (assignment.IsRefused(risk))
            { yield return Failure(request.RequestId, $"Plan posture refuses '{request.Capability}'."); yield break; }
            if (assignment.RequiresApproval(risk))
            {
                if (approvals is null)
                { yield return Failure(request.RequestId, "The approval service is unavailable."); yield break; }
                Guid.TryParse(session.BusinessId, out var organization);
                try
                {
                    admission = await approvals.AdmitAsync(organization, installation, request.Capability,
                        request.Payload.Span.ToArray(), session.Grant.Revision, risk, cancellationToken);
                }
                catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or Microsoft.EntityFrameworkCore.DbUpdateException)
                { denial = "The approval request could not be admitted. Refresh this agent's authority and retry."; }
                if (denial is not null || admission is null)
                { yield return Failure(request.RequestId, denial ?? "Approval admission is unavailable."); yield break; }
                if (admission.ResultsJson is not null)
                {
                    foreach (var stored in JsonSerializer.Deserialize<StoredCapabilityResult[]>(admission.ResultsJson) ?? [])
                        yield return new CapabilityResult { RequestId = request.RequestId, Succeeded = stored.Succeeded,
                            ContentType = stored.ContentType ?? "application/json", Error = stored.Error, Payload = JsonPayload.From(stored.Payload) };
                    yield break;
                }
                if (admission.Status != "Execute")
                {
                    yield return new CapabilityResult { RequestId = request.RequestId, Succeeded = false, ContentType = "application/json",
                        Error = $"Capability approval is {admission.Status}. Retry this exact request after approval; unconfirmed executions are never repeated automatically.",
                        Payload = JsonPayload.From(JsonSerializer.SerializeToUtf8Bytes(new { isError = true, approvalId = admission.ApprovalId,
                            state = admission.Status, retryAfterApproval = admission.Status is "Pending" or "Approved" })) };
                    yield break;
                }
            }
        }

        // Every admitted destructive/outward execution consumes a durable, tenant-scoped window.
        // The policy service serializes count+record across hosts before returning permission.
        if (temporalPolicy is not null && risk is CapabilityRiskClass.Destructive or CapabilityRiskClass.Outward)
        {
            // Without a posture reader nothing above validated the identity; refuse rather than throw.
            if (!Guid.TryParse(session.InstallationId, out var policyInstallation))
            { yield return Failure(request.RequestId, "The installation identity is invalid."); yield break; }
            var decision = await temporalPolicy.TryAdmitAsync(new AgentPolicyThreshold(risk.ToString(), TimeSpan.FromHours(1),
                risk == CapabilityRiskClass.Destructive ? 3 : 60),
                new AgentPolicyEvent(risk.ToString(), policyInstallation, session.BusinessId, risk, DateTimeOffset.UtcNow), cancellationToken);
            if (!decision.Allowed)
            {
                var result = Failure(request.RequestId, decision.Reason);
                if (admission is not null && approvals is not null)
                    await approvals.DeferAsync(admission.ApprovalId, cancellationToken);
                yield return result; yield break;
            }
        }

        {
            var approvedResults = admission is null ? null : new List<StoredCapabilityResult>();
            var receiptBytes = 0;
            await foreach (var result in handler.HandleAsync(session, request, cancellationToken))
            {
                string? retentionFailure=null;
                if(providerData!=null && result.Succeeded && Guid.TryParse(session.BusinessId,out var org) && Guid.TryParse(session.InstallationId,out var consumer) &&
                    (string.IsNullOrEmpty(result.ContentType) || result.ContentType.Contains("json",StringComparison.OrdinalIgnoreCase)))
                {
                    try { await providerData.RecordResponseAsync(org,consumer,result.Payload.Span.ToArray(),cancellationToken); }
                    catch(Exception e) when(e is UnauthorizedAccessException or InvalidOperationException or JsonException or Microsoft.EntityFrameworkCore.DbUpdateException)
                    { retentionFailure="The authorized data response is no longer available or exceeds its safe retention boundary. Refresh and request a smaller page."; }
                }
                var effective = retentionFailure is not null ? Failure(request.RequestId, retentionFailure) : result;
                if (approvedResults is null) yield return effective;
                else
                {
                    receiptBytes += effective.Payload.Length;
                    if (receiptBytes > 1024 * 1024) throw new InvalidOperationException("The execution result exceeds its durable receipt budget. Reconcile this operation before retrying.");
                    approvedResults.Add(Store(effective));
                }
            }
            if (approvedResults is not null)
            {
                if (approvedResults.Count == 0) approvedResults.Add(Store(Failure(request.RequestId, "The handler returned no result.")));
                await approvals!.CompleteAsync(admission!.ApprovalId, JsonSerializer.Serialize(approvedResults),
                    approvedResults.All(x => x.Succeeded), cancellationToken);
                foreach (var stored in approvedResults)
                    yield return new CapabilityResult { RequestId = request.RequestId, Succeeded = stored.Succeeded,
                        ContentType = stored.ContentType ?? "application/json", Error = stored.Error, Payload = JsonPayload.From(stored.Payload) };
            }
        }
    }

    private static StoredCapabilityResult Store(CapabilityResult result) =>
        new(result.Succeeded, result.ContentType, result.Error, result.Payload.Span.ToArray());

    private static bool IsImplicitPlatformCapability(string capability) =>
        capability is CSweet.Contracts.GenAi.GenAiCapabilities.JobRead or CSweet.Contracts.GenAi.GenAiCapabilities.JobCancel ||
        capability == AgentLifecycleCapabilities.CompleteOnboarding || McpToolCatalog.SelfReadCapabilities.Contains(capability);

    private static CapabilityResult Failure(string requestId, string error) => new()
    {
        RequestId = requestId,
        Succeeded = false,
        ContentType = "application/json",
        Error = error,
        Payload = JsonPayload.FromUtf8("{\"isError\":true}")
    };
}
