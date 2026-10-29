# C-Sweet — Deep UI/UX & Craft Review

**Date:** 2026-10-27
**Scope:** `src/CSweet.UI` (+ `src/CSweet.App` shell) — 203 `.razor` components, 139 stylesheets, 2,800-line `app.css`
**Method:** source read + rendered-screen review (README captures), measured against `docs/14-application-design-system.md`, an animation and interaction-craft bar, and responsive/performance audit criteria.
**Lenses:** design engineering, motion craft, responsive, accessibility, perceived performance.

---

## 1. Verdict

> **C-Sweet looks like a well-designed product and behaves like a static document.**

The information architecture is genuinely strong and the static visual layer is competent — but the app has almost no interaction layer. There is no press state, no arrival/departure motion, no loading continuity, and the type is a different typeface on every operating system. Those are the three cheapest things that separate "a really nice-looking internal tool" from "this feels expensive."

| Dimension | Score | Read |
| --- | --- | --- |
| Information architecture | **8** | One shell, nested business scope, attention badges. Rare discipline. |
| Static visual design | **7** | Clean hierarchy, good density on the board. |
| Brand coherence | **4** | Navy+mint is documented; purple+lavender shipped; gold is a third. |
| Design-system integrity | **4** | Tokens exist but arrive 47% into the file; 129 hex values. |
| Motion & feedback | **2** | 0 press states. 63 transitions, none where they matter. |
| Interaction craft | **5** | Correct semantics, wrong affordances (hover on inert messages). |
| Responsive / mobile | **5.5** | Excellent 44px work, broken at 320px, no safe areas. |
| Performance | **5** | WASM boot + `body:has()` theming + no virtualization on 167KB pages. |
| Accessibility | **7** | 84 focus-visible rules, 42 aria-live, reduced-motion in 30 files. |

---

## 2. What already works (do not regress)

1. **`@media (pointer: coarse)` 44px touch targets** are implemented properly in ~15 component stylesheets (`WorkBoardWorkspace.razor.css`, `MarketplaceAgentProfile.razor.css`, `CurrentActivity.razor.css`, …). Most apps of this size never get here.
2. **`prefers-reduced-motion` coverage is real** — 30 files, 38 declarations, and several use the correct `no-preference` gate rather than blanket disabling (`EmployeeConfigurationPanel.razor.css`, `EmailDeliverySetupStep.razor.css`).
3. **Semantics are right.** Real `<button>`, `aria-pressed` on toggles (`Appearance.razor`), `role="group"`, `role="status"`, 42 `aria-live` regions. The accessibility *machinery* is there; the focus *system* is not (see P2-17).
4. **The board screen is the best surface in the product** — clear eyebrow → title → tabs → sprint → columns, tabular numerals on progress, status color that means one thing.
5. **`color-mix()` used for theming** instead of pre-baked tints (`WorkBoardWorkspace.razor.css`, `Communications.razor.css`) — modern and correct.
6. **Container queries already in 5 files** — ahead of most Blazor codebases.

---

## 3. P0 — Blockers

### P0-1 · The product renders in a different typeface on every OS

`wwwroot/css/app.css` requests `Inter`, and `docs/14-application-design-system.md` mandates that stack. But there is **no `@font-face`, no Google Fonts link, and no bundled `.woff2` anywhere in `src/`** (verified: 0 matches for `@font-face`, 0 for `fonts.googleapis`).

What users actually get:

| Platform | Actual first match |
| --- | --- |
| macOS | Avenir Next (a geometric humanist — the screenshots) |
| Windows | Arial (the last entry; no Segoe in the stack) |
| Android | Roboto |

Every marketing capture was rendered on one machine; every customer sees a different product. This single fix changes the perceived quality of all 203 screens more than any other item in this document.

**Fix:** self-host Inter variable (`InterVariable.woff2`, ~90 KB, `font-display: swap`), preload in `index.html`, keep Avenir Next as the macOS fallback so existing captures stay honest.

---

### P0-2 · No press feedback anywhere in the app

- `:active` rules in `app.css`: **0**
- `:active` rules across all 139 stylesheets: **1**
- `:hover` rules in `app.css`: 26
- transition declarations: ~60 across 24 files

A click produces **no visual acknowledgement at all**. In a Blazor WASM app the round-trip is 100–400 ms, so the sequence is: user clicks *Hire* → nothing → 300 ms of doubt → second click → duplicate request. This is the highest-ROI defect in the codebase and it is a 15-line fix.

**Fix (universal, one rule):**

```css
@media (hover: hover) and (pointer: fine) {
  button:not(:disabled), [role="button"]:not([aria-disabled="true"]) {
    transition: background-color 140ms ease-out, border-color 140ms ease-out,
                box-shadow 140ms ease-out, transform 120ms ease-out;
  }
}
button:not(:disabled):active { transform: scale(.97); transition-duration: 80ms; }
```

Press-in at 80 ms (deliberate), release at 120–160 ms (system response). Asymmetric on purpose.

---

### P0-3 · Three competing color systems in one product

| Source | Says the brand is |
| --- | --- |
| `MainLayout.razor` (MudTheme) | navy `#18233F`, mint `#9BE7C2`, drawer `#0B2E53` |
| `app.css` + `1336–1341` | drawer `#231E36`, accent `#CBB5EF` — purple, `!important` |
| `app.css+` (aurora layer) | per-surface `--mud-palette-*` remap, lavender |
| `app.css` (gold theme) | black `#080807`, gold `#D8B976` |
| `docs/14` | navy + ivory + mint, coral for errors only |

The shipped screenshots are lavender/purple. The documented brand is navy/ivory/mint. **Neither the MudTheme nor the design doc can be trusted as the source of truth**, because an `!important` block 1,400 lines later wins.

This is not a taste argument — it is an ownership argument. Until one layer owns color, every new screen adds a fourth language.

**Fix:** delete the MudTheme palette literals from all three layouts; let `tokens.css` + a single `[data-appearance]` block drive both Mud variables and `--cs-*`. One file, one owner.

---

### P0-4 · No design-system middle layer

`:root` appears **once**, at `app.css` — 47% of the way into a 2,800-line file. Everything above it predates the token layer and is literal.

| Metric | Count | Doc requirement |
| --- | --- | --- |
| Unique hex values in `app.css` | **129** | tokens only |
| Distinct `border-radius` values | **13** (5,6,7,8,9,10,11,12,13,14,15,16,18,20,22) | 12px panel / 9px control |
| `font-size` declarations across UI | **969** | type scale |
| `!important` in `app.css` | 28 | exceptions documented |
| Tokens defined | 17 | — |

`docs/14` says: *"Do not duplicate a token as an unexplained literal in new CSS."* The code currently cannot honor that rule because there is no layer to hold the values.

---

### P0-5 · Horizontal overflow at 320px on the main chat surface

```css
/* Pages/Communications.razor.css and :214 */
.hub-page { grid-template-columns: 264px minmax(420px, 1fr); }        /* min 684px */
@media (max-width: 720px) {
    .hub-page { grid-template-columns: 82px minmax(280px, 1fr); }     /* min 362px */
}
```

At 320 px (iPhone SE 1st gen) and 360 px (Galaxy A-series — the largest Android segment in many markets) the second column cannot shrink below 280 px, so the layout is **362 px wide inside a 320 px viewport**, and `.hub-page { overflow: hidden }` means the right edge of every message is *clipped, not scrollable*.

**Fix:** `minmax(0, 1fr)` plus `grid-template-columns: 64px minmax(0,1fr)` at ≤480, and add a 320px assertion to CI.

---

## 4. P1 — High impact

### P1-6 · Motion is absent where it matters and present where it isn't

| Where | Current | Frequency of exposure |
| --- | --- | --- |
| Nav selection | instant | 100+/day |
| Drawer open/close (mobile) | MudBlazor default | 50+/day |
| Board drag & drop | instant snap | 30+/day |
| Message send | appears fully formed | 100+/day |
| List add/remove | instant | 50+/day |
| Dialog open | instant | 20+/day |
| **Marketplace card reveal** | **340 ms slide** | **rare** |
| **Hire success** | **8 s fade** | **once** |
| **Avatar presence pulse** | **2.6 s infinite** | **ambient** |

The two longest animations in the product are on the two rarest interactions. Exactly inverted.

### P1-7 · Hover on things that do nothing

```css
/* Communications.razor.css */
.hub-message.agent-accent .hub-message-bubble:hover { background: color-mix(...); }
```

Message bubbles are not clickable. The pointer changes their background anyway, so the app teaches "these are interactive" and then nothing happens. Same pattern on `.installed-agent-card:has(> .installed-agent-details-target:hover)` (`app.css`) — hover propagates from a child to a parent card.

### P1-8 · Hover is ungated for touch

`@media (hover: hover)` in CSS: **0 occurrences** (one in a JS file). Every `:hover` also fires on tap, and on mobile the style **sticks** after the tap — a tapped card stays greyed, a tapped tab stays hovered, until the user taps elsewhere.

### P1-9 · Breakpoint sprawl — 12 viewports, no vocabulary

`app.css` alone: `600, 640, 720, 721, 750, 800, 860, 900, 960, 961, 1100, 1200`, plus `@media (max-width:1200px) and (min-width:1051px), (max-width:760px)` and one container query at `900px`.

Consequences: 721 vs 720 (a one-pixel band where the app is in two states), 960 vs 961 (same), and no way to answer "what does this table do at 834?" without reading 139 files.

### P1-10 · The type scale is a list of ingredient sizes

969 `font-size` declarations, ~30 distinct values, rem and px mixed within the same component: `.57rem`, `.61rem`, `.65rem`, `.66rem`, `.68rem`, `.7rem`, `.72rem`, `.75rem`, `.78rem`, `.8rem`, `.82rem`, `.84rem`, `.86rem`, `.875rem`, `.9rem`, `11px`, `12px`, `13px`, `14px`, `16px`, `18px`, `20px`, `22px`, `24px`…

Net effect on a 27" display: **the entire application reads as footnotes**. Body copy in the chat composer is `.84rem` (≈13.4 px), metadata is `.61rem` (≈9.8 px) — below the readable floor.

---

## 5. P2 / P3

| # | Finding | Evidence |
| --- | --- | --- |
| P2-11 | **No maximum content width.** Full-bleed container; at 2560 px the board spans ~2,400 px and chat lines hit `min(85%, 64rem)` = 1024 px ≈ 110 characters (doc: 65). | `MainLayout.razor` `MaxWidth.False`; `Communications.razor.css` |
| P2-12 | **No `viewport-fit=cover`, zero `env(safe-area-inset-*)`.** The notch/gesture bar can never be handled; MAUI's shell already sets it, so web and desktop disagree. | `CSweet.App/wwwroot/index.html`; 0 matches for `env(safe-area` |
| P2-13 | **No `theme-color` meta** — mobile browser chrome is default grey above a purple app. | 0 matches |
| P2-14 | **No skeletons.** `skeleton` = 0 matches. Loading is a spinner or nothing; the doc requires layout-preserving loading. | `docs/14` Notifications and errors |
| P2-15 | **No list virtualization** — `Virtualize` = 0. Employees, agents, memory, conversations render fully. | — |
| P2-16 | **19 `<img>`, 4 `loading="lazy"`, 1 `srcset`.** | — |
| P2-17 | **Focus ring is 84 rules with 6 different styles** (2px/3px, offset −3..+3, four colors), and the **primary text input has no focus indicator at all**. | `Communications.razor.css` `outline: none` |
| P2-18 | `h1:focus { outline: none }` — template leftover, kills the skip-link target ring. | `app.css` |
| P2-19 | **`body:has(...)` theming.** 5-clause compound `:has()` lists on `body` re-map every Mud palette variable per surface; any class change in the subtree can invalidate them. | `app.css`, `2456–2457` |
| P3-20 | 28 `!important` in `app.css` (mostly drawer/palette fights). | — |
| P3-21 | Magic numbers: `left: -60px` on the display-options panel at 600 px. | `WorkBoardWorkspace.razor.css` |
| P3-22 | Three near-identical setup stylesheets (`GenAiProviderSetupStep`, `LlmProviderSetupStep`, `CommunicationsSetupStep`) duplicate the same primitives — ~1,000 lines of copy-paste that will drift. | — |

---

## 6. Motion review

### Findings table

| Before | After | Why |
| --- | --- | --- |
| No `:active` on any control | `transform: scale(.97)` @ 80ms in, 140ms out | A press with no response reads as a dropped tap; the user re-clicks and duplicates the action |
| `.hub-message-bubble:hover` background shift | Remove | The element is inert; hover implies action that doesn't exist |
| `:hover` effects with no media query | Wrap in `@media (hover: hover) and (pointer: fine)` | On touch the hover style sticks after tap, leaving cards/tabs in a false "active" look |
| `transition: transform 340ms cubic-bezier(.2,.8,.2,1)` (card reveal) | `220ms cubic-bezier(.32,.72,0,1)` | 340ms exceeds the 300ms UI ceiling; the reveal is a rare delight but it's a card, not a film |
| `animation: agent-hire-success-fade 8s ease-in forwards` | `220ms ease-out` on success, then auto-dismiss the toast | 8s `ease-in` is the slowest possible way to say "it worked"; `ease-in` delays the exact moment the user is watching |
| `transition: transform 340ms ease` on card image | `transform 240ms cubic-bezier(.32,.72,0,1)` | Built-in `ease` is too weak for a deliberate move; needs a stronger curve |
| No motion on nav selection | mint 3px marker `transform: translateY()` 200ms | The marker already exists as `box-shadow: inset 3px 0 0`; making it move ties the click to the destination |
| Board card drag: instant reposition | Lift `scale(1.02)` + shadow @ 140ms; settle @ 180ms | Direct-manipulation must respond inside one frame; the lift is the only feedback the card is "in hand" |
| Message send: bubble appears fully formed | `opacity 0→1` + `translateY(4px)`, 180ms ease-out, 30–80ms stagger for groups | Arrival without transition makes streaming replies look like a repaint glitch |
| Avatar `animation: 2.6s ease-in-out infinite` | Keep, but gate on `prefers-reduced-motion` and pause when offscreen | Ambient "alive" motion is the one place infinite loops earn their keep — if they're cheap and contextual |
| `transition: width` on progress bars | `transform: scaleX()` on an inner fill | Animating `width` triggers layout on every frame |

### Verdict

**Block.** Two feel-breaking regressions (no press feedback → duplicate actions; hover on inert surfaces → false affordance), plus non-GPU-adjacent motion choices (8s fade, 340ms card) on the wrong end of the frequency curve. Reduced-motion handling is the one part that passes today.

---

## 7. Buttons, interaction & creative latitude

Where there is room to make this feel expensive without adding risk. All of it is CSS-only or ≤10 lines of JS, all of it respects sub-300ms / `ease-out` / GPU-only / interruptible.

### 7.1 Signature press (do this first)

```css
:root {
  --press-in: 80ms;   --press-out: 140ms;
  --ease-out-strong: cubic-bezier(.22,.61,.36,1);
  --ease-spring: cubic-bezier(.34,1.4,.64,1);
}
button, [role="button"], summary {
  transition: background-color var(--press-out) var(--ease-out-strong),
              border-color var(--press-out) var(--ease-out-strong),
              box-shadow var(--press-out) var(--ease-out-strong),
              transform var(--press-out) var(--ease-out-strong);
}
button:not(:disabled):active { transform: scale(.97); transition-duration: var(--press-in); }
```

### 7.2 Destructive confirm: slow the deliberate half

Coral buttons get a 400 ms hold-to-confirm ring; the release cancels in 80 ms. Asymmetry makes the destructive path feel intentional rather than configurable.

### 7.3 Mint "seal" on success

Not a bounce. A 180 ms `scaleX` fill of the mint left-marker along the completed row, then a 600 ms fade of a ✓. One-shot, never on a list scroll.

### 7.4 Board card in hand

```css
.ticket { transition: transform 140ms var(--ease-out-strong), box-shadow 140ms var(--ease-out-strong); }
.ticket.dragging { transform: scale(1.02) rotate(-.4deg); box-shadow: 0 18px 40px -12px rgba(24,35,63,.28); }
```
Rotation is under half a degree — felt, not seen. Drop settles over 180 ms with the shadow collapsing faster (120 ms) than the scale, so it lands with weight.

### 7.5 Agent "thinking": the breathing orb

Replace the spinner during generation with a 3-dot/soft-orb that pulses at 1.8 s with a spring easing, and **stops the moment the first token arrives** — the transition from "thinking" to "writing" is itself the feedback. Springs are legitimate here (decorative, interruptible, alive), which is exactly the exception the standards allow.

### 7.6 Send: bubble arrival + streaming tail

Optimistic bubble at `opacity: .55` → `1` when the server echoes (200 ms). Streaming text should not re-animate; only the bubble shell enters, once.

### 7.7 View Transitions for board → ticket

`@view-transition { navigation: auto; }` in a `pageshow`-safe block, with the ticket card as `view-transition-name`. Browsers without support get the current instant cut. It's the single cheapest "this is modern" moment available in a Blazor app, and it costs one CSS rule + one attribute.

### 7.8 The nav marker should move

`.mud-nav-link.selected { box-shadow: inset 3px 0 0 var(--cs-mint) }` renders a new marker per item. Instead, one absolutely-positioned `.nav-marker` translated between `offsetTop`s over 200 ms — the mint line *travels* to your destination. One element, `transform` only.

---

## 8. Responsive matrix

| Width | What actually happens | Fix |
| --- | --- | --- |
| **320** | Chat overflow clipped (`hub-page` min 362px); board columns 240px fixed; 12px buttons below 44px | `minmax(0,1fr)`, sidebar 64px, column `min(78vw, 240px)` |
| **360–390** | Chat OK; composer 44px targets OK | Verify 16px input font (already correct in `WorkBoardWorkspace.razor.css`) |
| **430** | Fine | — |
| **768** | Board 4 columns collapse to horizontal scroll (intended) | Add scroll-snap + edge fade so the cut-off column is discoverable |
| **834/1024** | The 720↔720 and 960↔961 one-pixel bands | Collapse to 3 named breakpoints |
| **1280** | Chat comfortable | — |
| **1920** | Board fine, chat bubbles 1024px max | Cap content at 1440px |
| **2560** | Board 2,400px wide; lines >100 chars | `max-width: 1440px; margin-inline: auto` on `.content` |

Two systemic gaps: **no safe-area handling** (P2-12) and **no `hover:hover` gate** (P1-8).

---

## 9. Performance

| # | Issue | Cost | Fix |
| --- | --- | --- | --- |
| 1 | Blazor WASM cold boot, no prerender (`CSweet.App/Program.cs`) | 1–4 s blank-ish screen to first interaction | Prerender the shell; keep the existing loading SVG but add a skeleton of the destination layout |
| 2 | `body:has(...)` theme remaps (`app.css`) | Style invalidation from `body` on any subtree class change | Move to `:root[data-appearance]` + attribute on the surface, not `:has()` ancestry |
| 3 | 167 KB `WorkBoards.razor`, 125 KB `Communications.razor`, 106 KB `Agents.razor` | Every `StateHasChanged` re-diffs the whole page | Extract list rows into components with `ShouldRender`; today there is **1** `ShouldRender` override in the entire UI |
| 4 | No `Virtualize` on long lists | O(n) DOM on employees/agents/memory | `<Virtualize>` on the 4 longest lists |
| 5 | `PeriodicTimer(2s)` in two operation services + `CurrentActivity` clock | Wakes the circuit continuously during idle | Raise to 5 s or event-driven |
| 6 | 969 `font-size` + 129 colors | Larger CSSOM, slower recalc | Collapse to tokens (§10) |
| 7 | 4/19 images lazy | Wasted bytes on the marketplace grid | `loading="lazy"` + `decoding="async"` everywhere below the fold |

---

## 10. The missing middle layer

Proposed `wwwroot/css/tokens.css` (loaded before `app.css`, replacing `:root`):

```css
:root {
  /* brand */
  --cs-navy:#18233F; --cs-navy-deep:#0B2E53; --cs-ivory:#FFFCF7; --cs-surface:#FFF;
  --cs-mint:#9BE7C2; --cs-mint-strong:#23845C; --cs-amber:#F2B84B; --cs-coral:#E85D75;
  --cs-border:#E2DDD5; --cs-muted:#68738A;

  /* geometry — replaces 13 radii */
  --r-sm:6px; --r-md:9px; --r-lg:12px; --r-xl:16px; --r-full:999px;

  /* space — 4px rhythm, replaces arbitrary values */
  --s-1:.25rem; --s-2:.5rem; --s-3:.75rem; --s-4:1rem; --s-6:1.5rem; --s-8:2rem; --s-12:3rem;

  /* type — 7 steps, replaces ~30 sizes */
  --t-xs:.75rem; --t-sm:.8125rem; --t-base:.9375rem; --t-lg:1.125rem;
  --t-xl:1.375rem; --t-2xl:1.75rem; --t-3xl:2.25rem;

  /* motion */
  --d-instant:80ms; --d-fast:140ms; --d-base:200ms; --d-slow:260ms;
  --e-out:cubic-bezier(.22,.61,.36,1);
  --e-out-strong:cubic-bezier(.32,.72,0,1);
  --e-spring:cubic-bezier(.34,1.4,.64,1);
  --e-in-out-soft:cubic-bezier(.4,0,.2,1);

  /* elevation — 3 levels, replaces stacked shadows */
  --e1:0 1px 2px rgba(24,35,63,.06);
  --e2:0 8px 24px -8px rgba(24,35,63,.10);
  --e3:0 18px 44px -16px rgba(24,35,63,.18);

  /* breakpoint vocabulary (for container queries + docs) */
  --bp-sm:600px; --bp-md:900px; --bp-lg:1200px;
}
```

Then split `app.css` (2,800 lines) into `tokens.css` / `base.css` / `components.css` and delete the MudBlazor palette literals in the three layouts.

**Raising the floor:** `--t-base` at `.9375rem` (15 px) instead of the current `.72–.86rem` cluster is the single most legible change available — it will make the app look two years newer without touching a single layout.

---

## 11. Sequenced plan

### Phase 1 — Feel (1–2 days, no design decisions required)
1. Self-host Inter + preload (P0-1).
2. Global press state + asymmetry (P0-2).
3. Gate hover behind `(hover:hover) and (pointer:fine)`; delete inert hover (P1-7, P1-8).
4. One focus ring: `2px var(--cs-mint-strong); offset 2px` everywhere; restore composer focus (P2-17, P2-18).
5. Cap content at 1440px; fix 320px chat overflow (P0-5, P2-11).

### Phase 2 — System (3–5 days)
6. `tokens.css` + type scale; migrate the top 20 surfaces (P0-4, P1-10).
7. Single color owner; remove `!important` palette fights (P0-3).
8. Collapse 12 breakpoints → 3 (P1-9).
9. Theme via `:root[data-appearance]`, not `body:has()` (P2-19).

### Phase 3 — Life (3–4 days)
10. Nav marker travel, board lift, message arrival, agent orb (§7).
11. Skeletons for the 6 highest-traffic screens (P2-14).
12. `Viewize` the 4 longest lists; `ShouldRender` on the big three (P2-15, §9-3).
13. View Transitions board → ticket (§7.7).

---

## 12. Appendix — verification commands

```bash
# fonts
rg -c "@font-face|fonts.googleapis|\.woff2" src/          # expect: not 0 after fix
# press feedback
rg -c ":active" src/CSweet.UI --glob '*.css'               # expect: >= 20
# hover gating
rg -c "@media \(hover: hover\)" src/CSweet.UI --glob '*.css'  # expect: >= 10
# token drift
rg -o "#[0-9a-fA-F]{3,6}" src/CSweet.UI/wwwroot/css/app.css | sort -u | wc -l   # 129 → target < 20
rg -c "font-size" src/CSweet.UI --glob '*.css'             # 969 → target < 200
# 320px
rg "minmax\([3-9][0-9][0-9]px" src/CSweet.UI --glob '*.css'
```

---

*Reviewed against `docs/14-application-design-system.md` as the normative source. Findings that contradict that document are marked as deviations from it, not from personal preference.*
