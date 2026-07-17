# MProgressBar

**Location:** `Client/Components/Progress/MProgressBar.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`

## Purpose

Single shared progress indicator, replacing 7 independent `progress-bar`/`progress-fill`/`progress-track` implementations found by the Phase 1 audit. Maps directly to the dashboard's milestone/task progress UI (Phase 5 target). Not yet wired into any production page.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Value` | `double` | `0` | Current progress. Clamped to `[0, Max]`; `NaN`/`Infinity` treated as `0`. |
| `Max` | `double` | `100` | Clamped to `100` if `≤ 0` or non-finite. |
| `Label` | `string?` | `null` | Optional caption rendered above the track |
| `ShowValue` | `bool` | `false` | Renders the computed percentage next to `Label` |
| `AccessibleLabel` | `string?` | `null` | Overrides `Label` as the track's `aria-label` when they should differ |
| `Size` | `MProgressBar.ProgressSize` | `Medium` | `Small` / `Medium` / `Large` — track thickness |
| `Class` | `string?` | `null` | |

## Accessibility notes

- Track renders `role="progressbar"` with `aria-valuemin="0"`, `aria-valuemax`, and `aria-valuenow` (all reflecting the *clamped* value, never the raw, possibly-invalid input).
- `aria-label` is set from `AccessibleLabel` if provided, otherwise falls back to `Label`; if neither is set, the attribute is omitted entirely rather than rendering an empty `aria-label=""`.

## RTL behavior

The fill's `width` grows from the track's logical start edge because it is laid out as a normal block inside a `dir="rtl"` ancestor (no `left`/`right`/`transform: scaleX` tricks) — it fills toward the reading-start side automatically, no separate RTL rule needed.

## Usage examples

```razor
<MProgressBar Value="68" Label="פיתוח אב־טיפוס" ShowValue="true" />

<MProgressBar Value="_completedTasks" Max="_totalTasks"
              AccessibleLabel="התקדמות משימות השבוע"
              Size="MProgressBar.ProgressSize.Small" />
```

## Do / Don't

- **Do** pass `Max` when the denominator isn't 100 (e.g. task counts) — don't pre-compute a percentage yourself and pass it as `Value` with the default `Max`.
- **Do** use `AccessibleLabel` when the visible `Label` is decorative/short (e.g. just a percentage) and screen-reader users need more context.
- **Don't** wrap the rendered percentage text yourself for `ShowValue` — the component already formats and rounds it consistently.

## Known limitations / approved exceptions

- **Approved inline-style exception (per the Phase 3 spec):** the track element carries `style="--m-progress-percent: N%"` — a single narrowly-scoped CSS custom property, not a literal `width` inline style and not a generated per-percentage class. `MProgressBar.razor.css` reads it via `.m-progress-fill { width: var(--m-progress-percent, 0%); }`. This is the one documented exception in this phase; no other component uses an inline `style` attribute.
- Percentage is rounded to the nearest whole number for both the visual fill and the `ShowValue` text — sub-percent precision is not exposed.
