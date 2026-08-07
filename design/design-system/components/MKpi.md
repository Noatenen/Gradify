# MKpiGrid + MKpiCard

**Location:** `Client/Components/Kpi/MKpiGrid.razor`, `Client/Components/Kpi/MKpiCard.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "KPI card" / implementation reference row *KpiCard / KpiGrid*

## Purpose

The one KPI row in the system. The Master is explicit that this is a single shared component supporting "link, button, and pressed/filter states" — there is deliberately **no** `TasksKpiCard`, `RequestsKpiCard` or `DashboardKpiCard`.

## MKpiGrid

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Columns` | `int` | `4` | Clamped 1–4. Desktop column count |
| `AriaLabel` | `string?` | `null` | When set, the row becomes a named `role="group"` |
| `Class` | `string?` | `null` | |
| `ChildContent` | `RenderFragment?` | `null` | |

Responsive: 4 → 2 → 1 columns at 1060px / 520px, exactly as the Master specifies. Never 3+1.

**Width contract:** columns are `minmax(0, 1fr)` and the grid carries no max-width, so the KPI row shares the same horizontal boundaries as the section below it. The old fixed 208px per-card cap is the bug the Master's own audit closed — do not reintroduce it.

## MKpiCard

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Value` | `string?` | `null` | String, not int — "12", "3/8" and "—" are all valid; formatting stays with the caller |
| `Label` | `string?` | `null` | Always visible; colour is never the sole carrier of meaning |
| `Accent` | `MKpiCard.KpiAccent` | `None` | `None` / `Violet` / `Teal` / `Rose` / `Periwinkle` / `Cobalt` |
| `Icon` | `RenderFragment?` | `null` | Rendered in a tinted 30px chip, always `aria-hidden` |
| `ChildContent` | `RenderFragment?` | `null` | Optional supporting fact under the label |
| `Href` | `string?` | `null` | Renders `<a>`; takes precedence over `OnClick` |
| `OnClick` | `EventCallback<MouseEventArgs>` | — | Renders `<button type="button">` |
| `Pressed` | `bool?` | `null` | Filter/selected state. Nullable: `aria-pressed` is emitted only for real toggles |
| `AriaLabel` | `string?` | `null` | When Value + Label do not read well alone |
| `Class` | `string?` | `null` | |

With neither `Href` nor `OnClick`, the card renders an inert `<div>` — a non-interactive KPI can never look or behave clickable.

## Accessibility

Native `<a>` / `<button>` for the interactive forms (keyboard activation and focus order come from the element), `aria-pressed` only where the card is a toggle, visible `focus-visible` ring in `--motiva-color-focus-ring`, transitions disabled under `prefers-reduced-motion`.

## Implementation notes

- The accent ring is `color-mix(in srgb, <token> 45%, transparent)` so it follows the token into dark mode, with a static `rgba()` **preceding** declaration as the fallback (a `var()` fallback covers a missing value, not an invalid one).
- Hover and pressed thicken an `inset` ring rather than the border width — no layout shift.
- **`Accent="None"` rings in violet, not neutral** (added in Phase 4A, when Tasks became the first production consumer). An uncoloured card has no accent to ring with, and the neutral fallback (`--motiva-border-strong`) is one hairline step from its own resting border — a selected neutral card looked identical to an unselected one. It now uses the Master's focus/action violet for hover and pressed only; the resting border stays neutral, so an unselected uncoloured card is still visibly the uncoloured one. No API change.

## Accents are presentation, not status (Phase 4B)

`Periwinkle` (`#6E62B8`) and `Cobalt` (`#1D36E3`) were added when the finalized Tasks screen turned out to draw its four KPIs in four distinct hues. This does **not** loosen the three-semantic rule: the System Master scopes "three semantic colors only — violet / teal / rose — never introduce a fourth" to *StatusChip/StatusDot*, and `MStatusDot.DotTone` / `MStatusBadge` are unchanged and still closed to three.

Members are named for the hue, like the three before them, rather than for the metric using them — a `Scheduled` or `Team` member would bake one page's meaning into a shared component. New members are appended, never inserted, so no existing ordinal moved.
