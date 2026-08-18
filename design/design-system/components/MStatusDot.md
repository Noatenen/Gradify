# MStatusDot

**Location:** `Client/Components/StatusDot/MStatusDot.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "Status dots & chips" / implementation reference row *StatusChip / StatusDot*

## Purpose

The Master's 7px semantic dot: *"Three semantic colors only — violet / teal / rose — never introduce a fourth."* The enum has exactly those three members, so the rule is enforced by the API rather than by review.

| Tone | Meaning |
|---|---|
| `Violet` | active / in progress / focus |
| `Teal` | complete / on track |
| `Rose` | attention / blocked — reserved, never decorative |

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Tone` | `MStatusDot.DotTone` | `Violet` | |
| `Label` | `string?` | `null` | Visible text next to the dot, in the same semantic colour |
| `AriaLabel` | `string?` | `null` | Accessible name for a **bare** dot that carries meaning alone. Ignored when `Label` is set |
| `Pulse` | `bool` | `false` | The Master's attention ring — the expanding halo on a "requires attention" dot |
| `Class` | `string?` | `null` | |

## Accessibility

- With `Label`: the dot is `aria-hidden` and the text carries the meaning. This is the preferred form — a visible label serves everyone.
- Without `Label` and without `AriaLabel`: the dot is `aria-hidden="true"` (decorative reinforcement of a status stated elsewhere in the row).
- Without `Label` but with `AriaLabel`: `role="img"` + the name, for a dense row with no space for text.
- `Pulse` is removed entirely (`animation: none; display: none`) under `prefers-reduced-motion: reduce`, matching the Master's own stylesheet — an expanding halo is exactly what that setting exists to stop.

## Relationship to MStatusBadge

Dot = a marker inside a row. Badge = a pill that states the status in words. They use the same three semantics; pick the dot when the row's text already says what the item is, the badge when the status itself is the information.
