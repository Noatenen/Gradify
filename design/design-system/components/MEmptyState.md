# MEmptyState

**Location:** `Client/Components/EmptyState/MEmptyState.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "Empty state" / implementation reference row *EmptyState*

## Purpose

The Master: *"One line + one supporting line; never an illustration."* Centred, quiet, text-only — no border, no fill, no card treatment. An empty state is an absence and the Master never dresses it up.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Title` | `string?` | `null` | The calm sentence — what is not here |
| `Description` | `string?` | `null` | The supporting line — what will make it appear |
| `DescriptionContent` | `RenderFragment?` | `null` | Markup form; wins over `Description` |
| `Action` | `RenderFragment?` | `null` | One action, typically a single MButton |
| `Live` | `bool` | `false` | Adds `role="status"` + `aria-live="polite"` |
| `Class` | `string?` | `null` | |

## Deliberate omissions

- **No `Icon` / `Illustration` parameter.** The Master's rule is enforced by the API rather than left to each caller. Several existing student cards ship an icon in their empty state (`ttc-empty-icon`, `usc2-empty-icon`); those drop the icon when they migrate.
- **No default copy.** Every string is supplied by the page — nothing task-, request- or dashboard-specific is hardcoded.

## When to set `Live`

Only when the emptiness is the *result* of a user action — a filter that matched nothing, a list that just drained. A state present on first paint needs no announcement, and marking it live would make every page load chatter.
