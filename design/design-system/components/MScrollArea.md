# MScrollArea

**Location:** `Client/Components/ScrollArea/MScrollArea.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "Scrollable container"

## Purpose

The Master: *"Thin custom scrollbar (6px, low-contrast) — used inside fixed-height task/team panels."* Exists so pages stop re-declaring `::-webkit-scrollbar` rules per card — the prototype alone carries two identical copies (`.cardscroll`, `.teamscroll`), and the app has more.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `MaxHeight` | `string?` | `null` | CSS length, e.g. `"240px"`. Scrolls once content exceeds it |
| `Height` | `string?` | `null` | CSS length, when the panel must reserve its space |
| `Tabbable` | `bool` | `true` | Keyboard focusability of the scroll box |
| `AriaLabel` | `string?` | `null` | When set, exposes `role="region"` with that name |
| `Class` | `string?` | `null` | |
| `ChildContent` | `RenderFragment?` | `null` | |

`MaxHeight`/`Height` are emitted as an inline style on purpose: a panel's height is caller data, not a design token this component gets to decide.

## Accessibility

`tabindex="0"` by default — a scrollable region that cannot be focused is unreachable for anyone not using a pointer (WCAG 2.1.1). The focus ring is inset, since the scroll box normally sits flush inside a card. `role="region"` is added only when the region has a name; an unnamed region announces a boundary with nothing to identify it.

Set `Tabbable="false"` only when the content is itself fully focusable, which already provides a way to scroll.

## Deliberate deviation from the prototype (RTL)

The Master's screens force `direction: ltr` on the scroll box and `direction: rtl` back onto its **direct element children**, purely to move the scrollbar to the right in an RTL page. That breaks direction inheritance for anything that is not a direct child (bare text nodes, deeper subtrees) and is exactly the kind of hack a shared primitive should not spread.

This component keeps the inherited direction, so the scrollbar sits on the inline-start edge in RTL — the platform-native behaviour. The gutter uses `padding-inline-end`, which lands on the scrollbar's side in both directions.

## Implementation note

The thumb colour is derived from `--motiva-text-primary` with `color-mix` (the Master's ink at 16% / 26%) so it follows the token into dark mode instead of staying a light-mode `rgba()` on a dark surface. Each declaration is preceded by a static `rgba()` fallback for engines without `color-mix`.
