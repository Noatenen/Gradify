# MRowList + MRow

**Location:** `Client/Components/Row/MRowList.razor`, `Client/Components/Row/MRow.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "Row list (the 'table' pattern)" / implementation reference row *RowList*

## Purpose

The Master: *"Motiva has no literal `<table>`; every list is `.row` — space-between, hairline divider, no zebra striping … build once, no separate table component needed."*

This is that primitive, and it is deliberately generic: **no** task, request, submission or dashboard data shape reaches it, and it owns no status→colour mapping.

## MRowList

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Divided` | `bool` | `true` | Hairline between rows, none after the last |
| `AriaLabel` | `string?` | `null` | When set, exposes a named `role="group"` |
| `Class` | `string?` | `null` | |
| `ChildContent` | `RenderFragment?` | `null` | |

The divider is applied from the list side via `::deep`, with both list classes in the selector — MRow's own `border: 0` reset (needed for its `<button>` form) has the same specificity as a single-class `::deep` rule, so the extra class is what makes the outcome independent of scoped-stylesheet bundling order.

## MRow

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Leading` | `RenderFragment?` | `null` | Start slot — status dot, checkbox, avatar |
| `Primary` / `PrimaryContent` | `string?` / `RenderFragment?` | `null` | Main text |
| `Secondary` / `SecondaryContent` | `string?` / `RenderFragment?` | `null` | Quiet second line |
| `ChildContent` | `RenderFragment?` | `null` | Full override of the main block; wins over all four above |
| `Status` | `RenderFragment?` | `null` | End slot — typically MStatusDot / MStatusBadge |
| `Meta` | `string?` | `null` | End slot — date, count |
| `MetaTone` | `MRow.RowMetaTone` | `Default` | `Default` / `Violet` / `Teal` / `Rose`. The Master draws an overdue date in rose |
| `Trailing` | `RenderFragment?` | `null` | End slot — button, chevron |
| `Href` | `string?` | `null` | Renders `<a>`; wins over `OnClick` |
| `OnClick` | `EventCallback<MouseEventArgs>` | — | Renders `<button type="button">` |
| `Selected` | `bool` | `false` | Adds the Master's soft brand wash + `aria-current="true"` |
| `AriaLabel`, `Class` | `string?` | `null` | |

## Accessibility

Interactive rows are real `<a>`/`<button>` elements — never a click-only div. Focus ring is inset (`outline-offset: -2px`) because rows sit flush against their neighbours. `Selected` uses `aria-current` rather than `aria-selected`, which would require a listbox/grid parent this primitive deliberately does not impose.

## Known limitations

No built-in list semantics (`role="list"`/`listitem`). An interactive row's `<button>` role and a `listitem` role are mutually exclusive, and the Master's own markup is plain rows; a page that needs list semantics should wrap accordingly.
