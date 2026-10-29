# Agent handoff audit — 2026-10-28

> Follow-up corrections are described in [Agent governance](agent-governance.md). Findings below preserve the original audit evidence; they do not describe the corrected implementation.

## Verdict and scope

**Fail: do not treat the handoff as a completed or accepted implementation.** Reviewed agent session `100a685f-51a0-453f-8981-df5e1861adb0` through its 2026-10-28 12:19 UTC update and the current uncommitted C-Sweet checkout. This includes earlier agent work; a working-tree difference alone does not identify its author. This audit changes no product implementation and performs no deployment.

The strongest findings below were reproduced against the actual compiled dispatcher and posture service in an isolated console harness. No live agent, external service, or production data was used. A full unit-suite run is recorded separately below; failures must not all be attributed to this handoff without a baseline comparison.

## Blocking findings

### 1. P1 — Default Manual posture rejects work, and the API cannot change the host's posture

`PlatformCapabilityDispatcher.InvokeAsync` treats `RequiresApproval` exactly like a refusal, returning failure before the handler. `AgentPostureService.Default` is Manual. `DependencyInjection.AddCSweetInfrastructure` registers `InMemoryAgentPostureStore` as a singleton independently in each process; both API and AgentHost use that infrastructure registration. There is no generic approval admission/resumption path here.

Reproduction: set Auto in one `AgentPostureService`, construct the host service with its separately registered store, and invoke a granted `work.item.create` capability through `PlatformCapabilityDispatcher`. The host reads Manual and rejects the call. This disables normal granted mutations while providing no effective way for the operator to allow them through the API. A successful compilation does not validate this execution path.

Required correction: shared durable posture state and a real approval transition/resumption protocol, integrated without blocking already-authorized workflows. Do not globally switch to Auto to hide the failure.

### 2. P1 — Posture authorization grants too much authority and leaks cross-business state

`AgentPostureEndpoints` PUT only checks active business membership. Unlike `AgentManagementEndpoints.CanManageEmployeeConfigurationAsync`, it does not require Owner or a Manager's authority over the target employee. A regular member can request Auto for any installation in the business. GET has no business membership check at all; `AgentInstallationService.GetAsync` does an unscoped installation lookup and the response includes business/agent identities and the previous changer's application-user ID.

Required correction: apply the established employee-management authorization policy to mutation and an explicit authorized read policy to GET. Add owner/manager/member/outsider coverage. This is independently necessary even though the current process-local store prevents API changes from reaching AgentHost.

### 3. P1 — A real write is classified as Read and passes Plan mode

`CapabilityRiskCatalog` classifies `work.tracking.processed.v1` as Read. `DelegationCapabilityHandler` dispatches it to `TrackedDelegationService.ProcessedAsync`, which calls `Receipt` and adds a `WorkTrackingOperationReceipt` under `Mutate`.

Reproduction through the actual dispatcher with a probe handler: Plan + the granted capability reaches the handler successfully. Codemode's Read allowlist also accepts this classification, defeating its advertised read-only boundary when enabled.

Required correction: review classifications against implementations, not names, and test that every allowed read has the intended side-effect contract. At minimum this receipt-writing capability must not bypass Plan/read-only constraints.

### 4. P1 — Temporal policy is not enforced or fed by agent execution

`IAgentPolicyService` is registered and exposed through inspection/dry-run endpoints, but no production invocation calls `TryAdmitAsync` or records `AgentPolicyEvent` execution events. Its counts filter the new `agent.policy.*` event namespace, so ordinary existing audit entries do not automatically populate its budget. Neither the broker nor Office admission consumes this policy.

`docs/security/OFFICE-ATTESTATION-DECISION.md` calls temporal policy an interim mitigation that shipped. That claim is unsupported: the required caller and certification-recency rule are absent. The implemented policy is a windowed counter, not the proposed "formerly certified within 24 hours" authorization rule.

Required correction: integrate durable, idempotent policy admission/recording with execution and define the real supported rules before presenting this as protection. Process-local semaphore gates also do not make admission atomic across hosts.

### 5. P1 — The new delegation packet loses its handoff information and never reaches the existing worker flow

`CreateDelegationRequest` stores input artifacts and a return contract, but `DelegationResponse` and `DelegationEndpoints.ToResponse` omit both. There is no detail GET at the URL returned by Created. `DelegationCard` is not rendered by a page. Existing `DelegationCapabilityHandler` still uses `ITrackedDelegationService` and its separate tracked-work protocol; it never reads the new `Delegations` table. No work item or durable notification is created for these new packets.

Required correction: integrate the bounded packet with the existing tracked-delegation workflow, expose authorized complete state, and provide durable wake/reconnect recovery. Do not create a second, disconnected source of delegation truth.

## Other actionable findings

### 6. P2 — Delegation state and its audit record commit separately

Both creation and decision endpoints call `SaveChangesAsync` before `audit.AppendAsync`, without a surrounding transaction or notification outbox. An audit failure leaves committed work behind an error response; a retried create can create another packet because creation has no idempotency key. No atomic state-plus-notification operation is implemented, contrary to this repository's asynchronous-work requirements.

### 7. P2 — The operation tray and posture control are not integrated

`MainLayout` mounts `OperationTray` with `Array.Empty<OperationTray.OperationTrayItem>()`; it can never show an operation. `AgentPostureChip` has no real page consumer or API binding. These are component implementations, not completed operator features.

### 8. P2 — Codemode's output cap does not bound intermediate JSON expansion

`CodemodeCapabilityHandler.Resolve` deep-clones referenced JSON. Repeated bindings such as `let next = ["$previous", "$previous"]` can grow exponentially within the statement/input limits. The byte cap is applied only after `Render(emitted)`, and a single synchronous deep clone does not observe the cancellation token. This is a code-path finding, not a stress test: no large expansion was executed. Codemode is disabled by default, which limits current exposure; retain that default until memory/depth/work budgets are enforced during evaluation.

### 9. P2 — Contrast lifting does not cover elevated cards/menus

The theme generator lifts muted text against `surface` only. Independent calculation against `elevated` still finds: Solarized 3.69:1, Pearlescent 4.38:1, Off White 4.29:1, Dracula 4.06:1, Nord 3.89:1. Muted text is used on elevated surfaces. The assertion that all preset text meets the floor is therefore broader than the actual check.

## Verified positive changes

- The generator's `--check` succeeds; the catalog contains 43 distinct presets. All satisfy the generator's stated muted/faint floors against `surface`. The neutral Night Glass colors are retained.
- The email-confirmation change removes the obvious already-confirmed-account shortcut: the supplied token is now validated before signing in.
- The broker path checks now reject network-path references beginning with `//` at both the guest and forwarding layers.
- The Markdown pipeline removes GenericAttributes while keeping raw HTML disabled, addressing the identified attribute-injection path.
- The delegation migration creates the new table and indexes; compilation alone does not demonstrate that this workflow is integrated or the migration has been applied to a running database.

## Reproduction evidence

The temporary `csweet-governance-repro` console harness references the actual AgentHost project and produced:

```text
API set Auto; fresh host reads Manual
Default Manual: work.item.create => succeeded=False
Default Manual: work.tracking.processed.v1 => succeeded=True
Plan: processed write classified Read => dispatched=True
Undefined posture accepted by API parser: True; value=999
Catalog: 267 entries; handler called 2 times
```

The undefined numeric enum also needs validation: `AgentAuthority.TryParse("999")` succeeds, while `AgentPostureService.SetAsync` throws for that value rather than producing the endpoint's promised validation response.

## Build and test evidence

- Actual AgentHost/Application/Infrastructure/Contracts assemblies built as dependencies of the audit harness and test run.
- Theme generator `--check`: passed.
- Independent theme color calculations: surface floors pass; five elevated-surface muted-text failures remain.
- Full unit-suite command: `dotnet test tests/CSweet.UnitTests/CSweet.UnitTests.csproj -c GovernanceAuditVerified --logger "trx;LogFileName=governance-audit.trx"`.
- Full-suite result: **5,861 passed, 23 failed, 537 skipped; 6,421 total; 8 minutes 33 seconds**. TRX: `tests/CSweet.UnitTests/TestResults/governance-audit.trx`.
- Focused results within that run: AppearanceStateTests 11/11, RegistrationPageTests 11/11, AgentConfigurationVisibilityTests 3/3, ChatMarkdownLinkTests 4/4 passed.
- Thirteen failures are UI/resource-related: ten onboarding assertions still expect legacy `data-appearance=aurora/gold`, one settings assertion expects the removed Black & Gold label, one board assertion expects the old ungrammatical attention label, and one localization completeness test reports five missing zh-Hans resources. These are not evidence that the new palette should be reverted; reconcile the expected behavior while retaining legacy-input migration coverage.
- Ten other failures cover workspace storage admission (2), memory context validation (2), project schema/profile checks (2), approval attention (1), broker manager setup (1), architecture boundaries (1), and purge classification (1). The schema check explicitly depends on an absent sibling CreativeDirector repository. The purge classification expectation omits the plugin-account dependents now handled explicitly. Other failures need individual baseline/fixture analysis rather than being dismissed as environmental.
- No dedicated tests for the newly added posture endpoints/store integration, risk catalog, temporal policy, new delegation endpoints, or codemode were found in the unit suite. Many existing dispatcher tests omit the optional posture dependency, so they do not exercise the production default-Manual regression.

## Recommended continuation

Repair posture persistence/authorization/approval behavior and the Read classification first, with tests using the production DI path. Then connect temporal policy and delegation packets to existing durable execution flows. Wire the operator controls to real state. Only after those checks pass should live end-to-end acceptance or further UI iteration resume.
