# Motiva Design System — Component Review (Phase 3.5)

**Phase:** 3.5 — Component review and stabilization (pre-gallery, pre-adoption)
**Branch:** `feature/motiva-design-system`
**Scope:** `MButton`, `MCard`, `MStatusBadge`, `MProgressBar`, `MModal` under `Client/Components/`. No gallery created, no production screen migrated, no new components added, no folders moved, no backend touched.

Confirmed before review: zero production usages of any of the five components (`grep` across `Client/Pages` and `Client/Shared` returned no matches), so any API-shape change made in this pass has **zero migration cost** — this is the right and last cheap moment to fix conflated APIs.

---

## Method

Read foundations (`UI_ARCHITECTURE_AUDIT.md`, `MOTIVA_FOUNDATIONS.md`, `motiva-ui-library-v01.html`), the token files (`motiva-tokens.css`, `motiva-base.css`), all five components' `.razor`/`.razor.css`, and all five docs in `design/design-system/components/`. Reviewed against the 18-point checklist (API consistency, naming, enums, defaults, nullability, EventCallback usage, semantic HTML, keyboard behavior, accessibility, RTL, loading/disabled behavior, CSS isolation, token usage, hardcoded values, duplicate-ID risk, class-name-conflict risk, Razor ergonomics, adoption-lock-in risk) plus the component-specific checklist in the brief.

---

## Issues found

### Important — fixed

**1. MCard: `Interactive` was a `CardVariant` enum member, conflating visual style with interaction mode.**
`Variant` mixed two orthogonal concerns into one enum: `Default` / `Elevated` / `Ambient` are visual styles, but `Interactive` was a fourth member of the same enum, meaning a card could never be both `Ambient` *and* clickable, or `Elevated` *and* clickable — only one dimension could be expressed at a time. This is exactly the kind of decision that's cheap to fix now (zero adoption) and expensive later (a breaking enum-shape change across every call site once Phase 5 wires screens to it).
**Fix:** Removed `Interactive` from `CardVariant` (now `Default | Elevated | Ambient` only). Added an independent `[Parameter] public bool Interactive`. The root-class builder appends `m-card-interactive` alongside whatever `Variant` class is active, so e.g. `Variant="Ambient" Interactive="true"` now renders correctly — the CSS already supported this (`.m-card-interactive`'s rules were always additive, never mutually exclusive with `.m-card-elevated`/`.m-card-ambient`), so no CSS changes were needed. `OnClick` is now wired whenever `Interactive="true"`, regardless of `Variant`.
**Files:** `Client/Components/Card/MCard.razor`, `design/design-system/components/MCard.md`.

**2. MStatusBadge could render an accidentally-empty pill.**
If a caller passed neither `Text` nor `ChildContent` (e.g. a status-mapping bug returning a null label), the component still rendered the full `<span class="m-badge ...">` shell with an empty `m-badge-text` node — a colored pill with no visible text and no accessible name, silently violating the foundations' own rule that "semantic color must always pair with a non-color signal."
**Fix:** Added a `HasContent` guard (`ChildContent is not null || !string.IsNullOrWhiteSpace(Text)`) around the whole markup; the component now renders nothing at all when both are absent, rather than an empty pill.
**Files:** `Client/Components/Badge/MStatusBadge.razor`, `design/design-system/components/MStatusBadge.md`.

### Nice to have — documented, not fixed

**3. MButton: `Loading` uses the native `disabled` attribute, which can silently move focus.**
When a focused button transitions into `Loading`, the browser removes it from the tab order and can shift focus (often to `<body>`), which some screen reader users experience as a disorienting "focus vanished" moment, on top of the `aria-busy="true"` announcement. A pattern using `aria-disabled` + `pointer-events: none` instead of native `disabled` would preserve focus, but it's a real behavioral change (click-blocking would then rely entirely on the existing `HandleClickAsync` guard rather than the browser's native disabled semantics) that deserves its own sign-off rather than a silent fix inside a stabilization pass. Current behavior (native `disabled` + `aria-busy`) is a common, acceptable pattern (e.g. Bootstrap spinner buttons use the same approach) — not a defect, just a tradeoff worth knowing about before Phase 5 wires a lot of async save/submit buttons to it.

**4. MButton: `Type` is a raw `string`, not an enum, unlike `Variant`/`Size`.**
The brief specifically asked to evaluate `Variant`/`Size` (already enums, confirmed correct) — `Type` wasn't in scope for that question, and HTML's `type` attribute is case-insensitively matched by the browser regardless of casing typos, so the practical risk is low. Left as `string` to keep the parameter a thin pass-through of the native attribute; flagged here only for completeness.

**5. Three near-identical size enums (`ButtonSize`, `ProgressSize`, `ModalSize`) all define `Small`/`Medium`/`Large` independently.**
Not a functional bug — each is scoped to its own component and none currently need different members — but it's duplication that a shared `Size` enum could remove. Deferred rather than introduced now: doing so would mean creating a new shared-types location (a small architectural decision — where would it live? a new file under `Client/Components/`?) that's better made deliberately in a future phase than folded into a stabilization pass, per CLAUDE.md's "don't introduce new architectural patterns unless necessary."

**6. MCard: `OnClick` set without `Interactive="true"` fails silently.**
Even after fix #1, a caller can still pass `OnClick` while leaving `Interactive` at its `false` default; the callback is simply never wired, with no compile-time or run-time signal. A console warning would require JS interop (out of scope, per the Phase 3 no-new-JS constraint) or a Debug-only `Console.Error` (arguably over-engineering for a scenario a component gallery — Phase 4 — will catch visually). Documented explicitly in `MCard.md`'s Do/Don't list instead.

**7. MCard: interactive keydown does not `preventDefault()` on Space.**
Already documented in `MCard.md` prior to this review; confirmed still accurate and intentionally left alone — it matches the existing, already-shipped pattern in `TaskMilestoneAccordion.razor`, and fixing it well requires per-key `preventDefault` that Blazor doesn't support cleanly without risking Tab-navigation side effects.

**8. MModal: no focus trap, no body-scroll lock.**
Already documented in `MModal.md` prior to this review; confirmed still accurate. Matches every existing modal in the codebase (`MentorProfileModal`, `TaskDetailModal`) — none of them trap focus or lock scroll either, and the Phase 3 brief explicitly disallows adding a new JS dependency solely for this. Tracked as a follow-up, not fixed here.

### Confirmed correct — no action needed

- **MButton** `Type` defaults to `"button"` (never `"submit"`) — confirmed in `MButton.razor:35`.
- **MButton** `Loading` prevents repeated clicks via both the native `disabled` attribute and a defensive `IsEffectivelyDisabled` guard inside `HandleClickAsync` — double-protected.
- **MButton** `Disabled`/`Loading` behave consistently — both collapse into one `IsEffectivelyDisabled` computed property; callers never compute this themselves.
- **MButton** `Variant`/`Size` are already enums (`ButtonVariant`, `ButtonSize`), not strings — no change needed.
- **MCard** Enter/Space keyboard behavior on the interactive path is correct (`e.Key is "Enter" or " "`).
- **MCard** `Padding` is already an enum (`CardPadding`) — no change needed.
- **MStatusBadge** `Text`/`ChildContent` precedence is unambiguous (`ChildContent` wins if both set) and documented.
- **MStatusBadge** semantic variant fg/bg pairs are the same pairs authored together in `motiva-tokens.css` for contrast; no evidence of an actual contrast failure found.
- **MProgressBar** correctly handles `Max = 0` (falls back to 100), negative `Max` (falls back to 100), negative `Value` (clamped to 0), `Value > Max` (clamped to `SafeMax`), decimals (rounded once, at display time), and non-finite (`NaN`/`Infinity`) inputs on both `Value` and `Max`.
- **MProgressBar** `aria-valuenow`/`aria-valuemax` reflect the *clamped* `SafeValue`/`SafeMax`, never the raw input.
- **MProgressBar** displayed percentage matches the ARIA value (`Percent` computed once, from `SafeValue`/`SafeMax`).
- **MProgressBar** the `--m-progress-percent` custom property is written via `CultureInfo.InvariantCulture` — safe under non-`en-US` locales.
- **MModal** `_titleId` uses `Guid.NewGuid():N` per component instance — unique across simultaneously-rendered modal instances, no collision risk.
- **MModal** Escape handling only exists in the DOM while `IsOpen` (the whole overlay is `@if (IsOpen)`-gated) — cannot fire while closed.
- **MModal** backdrop clicks are correctly blocked from firing when clicking inside the dialog (`@onclick:stopPropagation="true"` on the dialog element).
- **MModal** close button has `aria-label="סגירה"`.
- **MModal** close button is explicitly `type="button"`, so `MModal` cannot accidentally submit a surrounding `<form>`.
- **MModal** focus is only forced on the closed→open transition (`_wasOpen` guard in `OnAfterRenderAsync`), not on every re-render — won't steal focus from an input inside the body while the modal stays open.
- No JavaScript interop was introduced anywhere in this component set (confirmed by inspection — `MModal` reuses `ElementReference.FocusAsync()`, a built-in Blazor API, matching the existing `MentorProfileModal.razor` pattern).

---

## Cross-cutting checklist results

| # | Item | Result |
|---|---|---|
| 1 | Public API consistency | Consistent: `Class`, `ChildContent`, `OnClick`/`OnClose` naming pattern held across all five |
| 2 | Parameter naming | Consistent, no abbreviations, matches `MOTIVA_FOUNDATIONS.md`'s naming rule in spirit |
| 3 | Enum naming/location | Consistent (nested public enum per component); duplication noted as #5 above, deferred |
| 4 | Default values | All sensible (`Primary`/`Medium`/`Default`/`Neutral`/`Medium`(Modal), `Type="button"`, `Max=100`) |
| 5 | Nullability | Consistent — `RenderFragment?`/`string?` optional, value types non-nullable with safe defaults |
| 6 | EventCallback usage | Correctly typed per event (`EventCallback<MouseEventArgs>` vs. plain `EventCallback`) |
| 7 | Semantic HTML | `<button>`, `role="dialog"`, `role="progressbar"`, `<span>` badge — appropriate given Blazor/HTML constraints; `MCard`'s interactive `role="button"` div is a deliberate, documented exception (a real `<button>` can't legally contain the arbitrary block-level `ChildContent` a card needs) |
| 8 | Keyboard behavior | Native for `MButton`; Enter/Space for `MCard` interactive; Escape for `MModal`; N/A for `MStatusBadge`/`MProgressBar` |
| 9 | Accessibility | See per-component notes above; two real gaps fixed (#1 is UX/architecture, #2 is a11y), rest documented |
| 10 | RTL behavior | No hardcoded `left`/`right` found anywhere in the five stylesheets; logical properties/gap used throughout |
| 11 | Loading/disabled | `MButton` only component with this concept; consistent, double-guarded (see #3 above for the one nuance) |
| 12 | CSS isolation | All five have their own scoped `.razor.css`; Blazor's `b-xxxx` scoping prevents leakage regardless of shared `m-` prefix |
| 13 | Token usage | 100% token-driven; only two documented literal exceptions (`color: #fff` on gradient/danger button text, modal backdrop scrim), both explained in-file and in docs |
| 14 | Hardcoded visual values | None beyond the two documented exceptions above |
| 15 | Duplicate-ID risk | Only `MModal` generates an ID; confirmed GUID-unique per instance |
| 16 | Class-name conflicts | Each component uses a distinct prefix (`m-btn`, `m-card`, `m-badge`, `m-progress`, `m-modal`); Blazor CSS isolation is a second layer of protection regardless |
| 17 | Razor-page ergonomics | Good — global `@using AuthWithAdmin.Client.Components` already wired in `_Imports.razor`; nested enums require a slightly verbose `MButton.ButtonVariant.Primary` qualifier but this is a normal, well-precedented Blazor pattern and matches the docs' usage examples |
| 18 | Hard-to-change-later risk | The one real instance found was MCard's `Interactive`-as-`Variant` conflation — fixed now, at zero cost, precisely because it's the kind of thing that's free today and a breaking change after Phase 5 |

---

## Changes made

| File | Change |
|---|---|
| `Client/Components/Card/MCard.razor` | Removed `Interactive` from `CardVariant` enum; added independent `[Parameter] public bool Interactive`; updated markup condition and `RootClass` builder accordingly |
| `Client/Components/Badge/MStatusBadge.razor` | Added `HasContent` guard; component now renders nothing when both `Text` and `ChildContent` are absent |
| `design/design-system/components/MCard.md` | Updated variant table, parameter table, accessibility notes, usage examples, and Do/Don't to reflect the `Interactive` split |
| `design/design-system/components/MStatusBadge.md` | Documented the new empty-render guard in Accessibility notes and Do/Don't |

**API changes:** Yes — `MCard.CardVariant` lost a member (`Interactive`), and `MCard` gained a new `bool Interactive` parameter. This is a breaking change to the enum shape, but since zero code anywhere in the repo consumes `MCard` yet (confirmed by grep before starting), it breaks nothing that exists today.

**Breaking changes:** Only the above, and only in the sense of "would have broken something had adoption already started" — no adoption exists, so no working code was broken by this pass.

---

## Deferred improvements (not fixed, tracked for later)

- MButton `Loading`'s use of native `disabled` vs. `aria-disabled` (item #3) — worth a deliberate design decision before Phase 5 wires many async buttons to it, not a silent fix here.
- Shared `Size` enum across `MButton`/`MProgressBar`/`MModal` instead of three separate `ButtonSize`/`ProgressSize`/`ModalSize` enums (item #5) — a deliberate future architecture decision, not introduced unprompted.
- MCard silently ignoring `OnClick` without `Interactive="true"` (item #6) — documented; a Phase 4 gallery pass should visually catch any real misuse before it reaches production.
- MCard interactive Space-key scroll (item #7) — pre-existing, matches codebase precedent, not solved elsewhere either.
- MModal focus trap and body-scroll lock (item #8) — pre-existing gap matching every other modal in the codebase; requires either JS interop or a newer Blazor primitive, explicitly out of scope for Phase 3.

---

## Folder structure recommendation

**Recommendation: keep `Client/Components/` as-is. Do not move to `Client/DesignSystem/`, `Client/Motiva/`, or `Client/Components/Motiva/` at this time.**

Reasoning:

1. **Existing project convention.** The Phase 1 audit (`UI_ARCHITECTURE_AUDIT.md`, section G) explicitly proposed this exact path (`Client/Components/Button/MButton.razor(.css)`, etc.) after surveying the whole codebase for precedent, and Phase 3 followed it exactly. There is no competing convention to reconcile with — `Client/Shared/` (layout/chrome) and `Client/Pages/` (routes) are the only other top-level groupings, and neither overlaps in purpose with a generic component library.
2. **Namespace clarity.** Every component already declares `@namespace AuthWithAdmin.Client.Components`, which is already globally imported via `_Imports.razor`. The folder name matches the namespace exactly today. Moving to `Client/Motiva/` or `Client/DesignSystem/` without also renaming the namespace would create a folder/namespace mismatch — worse for discoverability, not better. Renaming the namespace to match would be a second breaking change layered on top of a folder move, for a benefit (branding the folder "Motiva") that doesn't change how the code is consumed.
3. **Discoverability.** `Client/Components/` currently contains *only* these five Motiva primitives (confirmed by directory listing) — there is no ambiguity today about what "Components" means in this codebase, and no other kind of shared, generic UI component exists to compete for the name.
4. **Migration risk.** Moving now costs nothing (zero adoption), but that argument cuts both ways — it also means there's no forcing function to move it *later* either. The real risk calculus is: moving *after* Phase 5 wires dozens of pages to `AuthWithAdmin.Client.Components` would be pure, avoidable churn (find/replace across every consuming page) for a purely cosmetic rename. Since the current name already reads clearly and matches namespace + convention, there's no benefit large enough to justify banking that future risk.

If a second, genuinely different category of shared-but-not-design-system component ever emerges (e.g. a generic non-visual utility component), `Client/Components/Motiva/` becomes the right move at that point, to disambiguate. Not needed today.

---

## Readiness decision

All Important-severity issues found were fixed. No Critical issues were found — nothing crashes, no XSS/injection risk, no null-reference risk, no duplicate-ID risk, no accessibility failure that silently produces an unusable interface. The one architectural conflation found (MCard's `Interactive`) was caught and corrected before any adoption cost existed. Remaining items are genuinely Nice-to-have and are documented rather than fixed, per the brief's instruction to leave nice-to-haves unless the fix is small and safe (the two that were both small, safe, *and* had real user/dev-facing impact were fixed; the rest require either a deliberate design decision or out-of-scope JS interop).

Build: `dotnet build AuthWithAdmin.sln --no-incremental` → **0 errors, 127 warnings** (all pre-existing, Server-side nullable-reference warnings unrelated to this review — same count as the Phase 1 audit's baseline).

**Component review complete. Ready for Phase 4.**
