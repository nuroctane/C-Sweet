# C-Sweet — Pass 2: Principles transferred from Tunerz Society

**Date:** 2026-10-27
**Reference:** `tuner-society` (`src/style.css` 806 lines, `src/*.ts`)
**Subject:** `src/CSweet.UI`
**Relationship to pass 1:** disjoint. Pass 1 covered fonts, press states, colour ownership, tokens, motion inversion, hover gating, breakpoints, type scale, width caps, safe areas, skeletons, virtualization, focus rings. **None of that is repeated here.** This pass asks a different question: *what does a genuinely great site do structurally that this one doesn't.*

---

## 1. The reference, in twelve rules

Read off the source, not off taste:

| # | Rule | Evidence in `tuner-society` |
| --- | --- | --- |
| 1 | **One curve, few durations** | `--ease: cubic-bezier(.22,1,.36,1)` at `:root`; the whole site uses it. Durations cluster at 150 / 160 / 180 / 260 / 300 / 420. |
| 2 | **Every button answers a press** | `.button:active { transform: scale(.97) }` — global, one line |
| 3 | **Hover is gated** | `@media (hover: hover) and (pointer: fine)` — two blocks, near the top, plus a JS `FINE_POINTER_QUERY` |
| 4 | **Two preference axes honored** | `prefers-reduced-motion` kills transitions, animations, smooth scroll, marquee, magnet and confetti; **`prefers-reduced-transparency`** swaps `backdrop-filter` for solid ink |
| 5 | **Reserve the box before the content** | *"Every card shares the frame's 4:5 box, so the space is reserved before an interior has loaded and no card can shift the layout"* |
| 6 | **Layout follows the container** | *"Columns follow the width the wall actually has, not the viewport"* |
| 7 | **Enhancement has a watchdog** | Inline script adds `site-enhanced`; if `site-ready` hasn't landed in **4 s**, it removes it and the plain page stands |
| 8 | **The page knows what time it is** | *"Upcoming-only rule slides, removed with the hero when `endAt` passes"* |
| 9 | **Live data doesn't destroy what you're looking at** | Refresh compares *"what the visitor sees (photos and handles), not markup"*; a failed fetch keeps the pre-rendered copy |
| 10 | **Gestures are physical and interruptible** | `REACH 92px / FLICK 520px/s / VLICK 90ms velocity window`; `.is-leaving` 300 ms against 420 ms enter; `.is-snapping { transition: none }` |
| 11 | **Performance is measured, not assumed** | `afterPaint()` exists because *"building an oscillator graph … pushed tap latency (INP) past 200 ms at the 95th percentile"*; marquee holds still off-screen *"so a long read further down costs no frames"* |
| 12 | **Copy is counted and plural-aware** | `` `${count} ${count === 1 ? "build" : "builds"}` `` |

---

## 2. Findings

### N1 · Boot has no failure state — the app cannot tell you it failed

**Reference (rule 7).** `index.html` ships a 4-second watchdog. If the enhancement never lands, the class is removed and the pre-rendered page is *still the product*.

**C-Sweet.** `CSweet.App/wwwroot/index.html` renders a `.loading-progress` spinner and nothing else. There is no timeout, no script, no retry, no copy. `#blazor-error-ui` (`app.css`) only covers exceptions *after* the WASM runtime has started. If the assembly 404s, the tunnel is down, or the launcher died, the user watches concentric circles forever.

This is not hypothetical for a self-hosted product: `README.md` tells users to *"keep its terminal open while using C-Sweet"* — the failure mode the README warns about is exactly the one the UI cannot report.

**Cost.** A hang and a boot failure are visually identical. The user's only recourse is a reload loop.

**Fix.** ~12 lines in `index.html`: after 8 s, replace the spinner with *"C-Sweet isn't responding. Check that the launcher window is still running."* + a Reload button. Mirrors the reference exactly, including the "leave the fallback alone" behaviour.

---

### N2 · The signature interaction is pointer-only

**Reference (rule 10).** The swipe deck is explicitly dual-input: *"Keyboard is the same deck as the pointer: left is next, right is back, Escape returns to the flyer."*

**C-Sweet.** Moving a card between columns is HTML5 drag:

```razor
<!-- WorkBoardWorkspace.razor -->
<button class="ticket-drag" draggable="true" @ondragstart="@(() => _draggedItem = item)" …
        aria-label="Drag @item.Identifier; open item for Move or assign actions">
```

`aria-grabbed`: 0 occurrences. No keyboard move, no drop announcements, no `role="listbox"` semantics. The accessible name does route keyboard users to a fallback (*"open item for Move or assign actions"*) — which is better than most kanban implementations and deserves credit — but the fallback costs 4 interactions where the mouse costs 1.

**Cost.** The 30×/day action has no parity path, and a drag that fails (dropped outside a column) announces nothing.

**Fix.** A `Move to…` menu on card focus with `M`, plus an `aria-live="polite"` announcement on every status change ("CS-104 moved to In progress"). Reuse the existing status list; no new UI.

---

### N3 · Counts are ungrammatical — including the accessible name

**Reference (rule 12).** Singular/plural is handled at every count.

**C-Sweet.**

```razor
<!-- WorkBoardWorkspace.razor -->
<button … aria-label="@($"{AttentionCount} need attention")"
        title="@($"{AttentionCount} need attention")">@AttentionCount</button>
```

This is what produced **"0 need attention"** in `assets/readme/screenshots/software-board.png`. It is also the button's *accessible name*, so a screen reader announces "zero need attention". `1 need attention` is equally reachable.

Across the whole UI there is **no plural handling at all** (`== 1 ?` → 0 matches). A workspace with counts on approvals, unread messages and attention is a workspace that will show "1 approvals" eventually.

**Fix.** Three strings, plural-aware, localized: `No items need attention` / `1 item needs attention` / `{count} items need attention`. The nav badges (`NavMenu.razor`) have the same problem in the `aria-label` only.

---

### N4 · No skip link, and the one focus target is invisible

**Reference (rule 4/3).** A `.skip-link` that translates into view on focus — 160 ms, `var(--ease)`.

**C-Sweet.** `skip-link` / "skip to content": **0 occurrences**. The shell is a persistent 260 px drawer with ~15 links plus the business switcher; every keyboard user tabs all of it, on every navigation.

Worse, the escape hatch is disabled: `Routes.razor` correctly ships `<FocusOnNavigate Selector="h1" />`, but `app.css` says

```css
h1:focus { outline: none; }
```

so the thing that receives focus on every route change shows no ring. The two mechanisms cancel each other.

**Fix.** Add the skip link (5 lines, copy the reference's CSS), and change `h1:focus` to `h1:focus-visible`.

---

### N5 · Arabic ships, and RTL is broken by physical properties

**Reference (rule 6).** Layout is expressed in terms of the container and the content, not a fixed side.

**C-Sweet.** `SupportedCultures.cs` ships `new("ar", "Arabic", "العربية", true)`, and `AppCulture.cs` applies `dir="rtl"` on change. So RTL is a supported, selectable, shipped locale.

The CSS is written physically:

| | count |
| --- | --- |
| `margin-left` / `margin-right` | **201 / 201** |
| `margin-inline-start` / `-end` | 23 |
| `padding-left` / `padding-right` | **201 / 201** |
| `padding-inline-start` / `-end` | 13 |

≈ **9:1 physical**. Concrete breakages: the grouped-bubble tails are `border-top-left-radius: 5px` (`Communications.razor.css`), so in Arabic the tail points to the wrong side; `margin-left: auto` on nav badges (`Communications.razor.css`, `NavMenu.razor.css`) pushes them against the wrong edge.

**Cost.** Arabic is selectable today and will render visibly incorrect. That is a shipped bug, not a roadmap item.

**Fix.** Either sweep to logical properties (`margin-inline-start`, `border-start-start-radius`) — mechanical, ~400 declarations — or remove `ar` from `SupportedCultures` until it's done. One of the two; not neither.

---

### N6 · The chat yanks the reader

**Reference (rule 9).** Live refresh is written so the visitor's view is preserved; failure keeps the old copy.

**C-Sweet.**

```js
// wwwroot/js/communications.js
export function scrollToBottom(element) {
    if (!element) return;
    element.scrollTop = element.scrollHeight;   // unconditional
}
```

```csharp
// Communications.razor
if (!_scrollToBottomPending) return;
_scrollToBottomPending = false;
await module.InvokeVoidAsync("scrollToBottom", _messageList);
```

There is no *"is the reader near the bottom?"* test. A message arriving while someone is reading history scrolls them to the newest one. In an agent workspace — where an agent posts several messages in a burst — the reader is pulled down repeatedly.

**Fix.** Guard on distance: only auto-scroll when `scrollHeight - scrollTop - clientHeight < 80`. Otherwise show a floating "3 new messages ↓" pill that scrolls on click. This is the standard and it costs ~15 lines.

---

### N7 · Sound fires while you are already looking at the thing

**Reference (rule 9 + 11).** `if (document.hidden) return;` — the cue layer refuses to work for an invisible page.

**C-Sweet.** `Services/NotificationSounds.cs` is genuinely well-built on the inside: master switch, volume, 1.5 s per-event de-duplication (`MinimumIntervalMilliseconds`), silence by empty URL, failures never surface. That design is above average.

What it lacks is *context*: `document.hidden` appears **0 times** in the UI project. The chime plays whether the tab is focused and the user is staring at the message, or the tab is buried behind a meeting. There is no per-user mute — `Enabled` is an operator/configuration switch (`CSweet:NotificationSounds:Sounds`), not a user preference.

**Cost.** A user in a call cannot stop the app from chiming; the app chimes hardest exactly when the chime is useless.

**Fix.** Gate on `document.hidden || !document.hasFocus()` inside the player, and add one user-level toggle next to the existing sound settings in `Appearance`/`CommunicationSettings`.

---

### N8 · Space isn't reserved, so lists move under the pointer

**Reference (rule 5).** The 4:5 frame is reserved before the interior loads — *"no card can shift the layout"*.

**C-Sweet.** 19 `<img>` tags; **one** carries `width`/`height` (`PluginCatalogIcon.razor`, 32×24). Zero portraits declare `aspect-ratio` in CSS. `PersonAvatarContent.razor`, `EmployeeAvatar`, `InstalledAgentCard.razor` and `MarketplaceAgentCard.razor` all render a portrait whose box is defined by the image once it arrives.

The nav badges make the same mistake in layout terms: `.communications-nav-badge` is rendered only `@if (count > 0)`, so a badge appearing reflows the row.

**Cost.** CLS on every list that contains people — and this product's entire model is *people and agents with portraits*.

**Fix.** `aspect-ratio: 1; width: <token>; height: <token>` on the avatar classes; reserve the badge slot with `min-width` and `visibility`, not `@if`.

---

### N9 · Blur with no transparency fallback

**Reference (rule 4b).** `@media (prefers-reduced-transparency: reduce)` strips `backdrop-filter` from every glass surface and drops it to solid ink.

**C-Sweet.** 7 `backdrop-filter` uses (`MainLayout.razor.css`, `app.css`, `CommandCenter.razor.css`, `Projects.razor.css`, `Communications.razor.css`, `Setup.razor.css`) and **0** `prefers-reduced-transparency` rules.

**Cost.** Users who enable reduced transparency — a real setting on Windows and macOS, and the one that matters for motion-sensitivity and low vision — get blurred glass in the app bar, dialogs, loading overlay and widgets anyway.

**Fix.** One block, ~8 selectors. Copy the reference.

---

### N10 · Nothing records *why*, so the design system has no memory

**Reference (rule 11 + the comments).** `style.css` carries 30 comment blocks in 806 lines, and they are tradeoffs, not labels: *"off screen the marquee holds still … so a long read further down costs no frames"*, *"the stage clips a card on its way out; the frame itself stays unclipped so its offset border can sit outside"*, *"looser than the section headings on purpose: Impact collides at -.065em"*.

**C-Sweet.** `app.css`: 28 comment blocks in 2,800 lines (one per 100 lines, and most are section labels). `WorkBoardWorkspace.razor.css`: **1** in 190 lines.

That is the mechanism behind pass 1's numbers. 129 hex values and 12 breakpoints exist because no one wrote down why the 12th was needed. Comments are how a design system retains decisions across contributors.

**Fix.** Rule in the PR template: *any value that is not a token carries a one-line reason above it.* Two examples: `/* 340ms: the card slides from 101%, faster reads as a cut */`, `/* 82px rail at ≤720 because labels can't fit */`.

**And no measurement.** The reference instruments because it decided a budget (`INP < 200 ms`) and then found its violation. C-Sweet has no LCP/INP/CLS capture anywhere — the only timing guard is a 10 s navigation timeout (`Routes.razor`). You cannot defend a budget you never take.

**Fix.** A ~30-line RUM beacon posting LCP/INP/CLS for five routes (`/`, board, communications, employees, marketplace) into the existing telemetry.

---

## 3. Already at the reference bar — keep these

These are not small, and the findings above should not obscure them:

| Practice | Evidence |
| --- | --- |
| **Stable list identity** | `@key` used **102 times** in Razor — realtime updates keep DOM identity, focus and scroll |
| **Focus managed on navigation** | `<FocusOnNavigate Selector="h1" />` (`Routes.razor`) |
| **Auth fails closed, with a timeout** | `GuardNavigationAsync` — 10 s linked token, and on failure *"the user is treated as signed out; the login page explains the outage"* (`Routes.razor`) |
| **Empty states that say what to do next** | *"Your company conversations live here / Choose a channel, or start a direct message with a teammate."* (`Communications.razor`) |
| **Sound de-duplication and silence-by-config** | `NotificationSoundOptions.MinimumIntervalMilliseconds`, empty URL silences |
| **Modern colour math instead of pre-baked tints** | `color-mix()` in `WorkBoardWorkspace.razor.css`, `Communications.razor.css` |
| **Touch parity** | `@media (pointer: coarse)` 44 px targets in ~15 files |
| **Reduced-motion, correctly** | 30 files; `no-preference` gates rather than blanket disabling |

---

## 4. Order of work

| | Item | Effort | Why first |
| --- | --- | --- | --- |
| 1 | **N1** boot watchdog | 30 min | Turns an invisible hang into a sentence |
| 2 | **N3** count copy | 1 h | Ships in the README screenshot; it's the first thing anyone reads |
| 3 | **N6** scroll guard | 2 h | The chat is the product; being yanked is felt every day |
| 4 | **N8** avatar boxes | 2 h | Kills CLS across every people surface |
| 5 | **N4** skip link + `:focus-visible` | 1 h | One-line fix, whole-app accessibility win |
| 6 | **N9** transparency fallback | 1 h | 8 selectors |
| 7 | **N7** sound context gating | 2 h | Removes an embarrassment in meetings |
| 8 | **N2** keyboard move + announcements | 1 d | Parity for the signature interaction |
| 9 | **N5** RTL sweep *or* drop `ar` | 1–2 d | Must be one or the other; shipping it broken is not an option |
| 10 | **N10** rationale rule + RUM | ongoing | Prevents pass 3 |

---

## Status — 2026-10-29

| Item | State |
| --- | --- |
| N1 boot watchdog | Done: `index.html` replaces the spinner after 12 s with a reload prompt. |
| N2 keyboard move | Done: the card handle in `WorkBoardWorkspace` moves with Left/Right (`MoveByKeyAsync`); a polite live region announces moves and refused drops. |
| N3 counts | Done for `WorkBoardWorkspace`, `OperationTray`, `PendingAgentQuestions`, `GitHubConnectionCard`, `RepositoryBusinessOnboarding` and hire labels; nav badges use ICU plurals. |
| N4 skip link / focus ring | Done: skip link present; `h1:focus-visible`. |
| N5 RTL | Done for margin, padding, border sides, corner radii, `text-align` and `float` (logical properties across 68 stylesheets); setup and registration layouts now use `MudRTLProvider`. Physical `left`/`right` offsets remain where centring uses `translateX`. |
| N6 scroll guard | Done: `scrollToBottom` only follows when within 80 px of the end. |
| N7 sound context | Done: chimes play only when the tab is hidden or unfocused; a per-browser mute is on the Appearance page. |
| N8 avatar boxes | Portraits already have fixed CSS boxes; no change needed. |
| N9 transparency fallback | Done: `prefers-reduced-transparency` rules present. |
| N10 rationale rule / RUM | Open. |
| P1-7 (pass 1) hover on bubbles | Done: hover styling removed from non-interactive message bubbles. |
| P1-8 (pass 1) touch-gated hover | Open: about 180 `:hover` rules need review, not a mechanical sweep. |

## 5. Appendix — assertions to re-run

```bash
rg -c "prefers-reduced-transparency" src/CSweet.UI        # 0 today
rg -c "document.hidden" src/CSweet.UI                     # 0 today
rg -c "skip-link" src/                                  # 0 today
rg -c "aria-grabbed" src/CSweet.UI                        # 0 today
rg -c "margin-inline|padding-inline" src/CSweet.UI        # 36 today
rg -c "margin-left|padding-left" src/CSweet.UI            # 402 today
rg -c "== 1 ?" src/CSweet.UI --glob '*.razor'             # 0 today
rg -c "aspect-ratio" src/CSweet.UI --glob '*.css'         # check per avatar class
```

---

*Pass 2 measures C-Sweet against a shipped site's operating principles, not against preference. Every finding cites the line that would have to change.*
