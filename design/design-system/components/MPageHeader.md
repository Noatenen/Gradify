# MPageHeader

**Location:** `Client/Components/PageHeader/MPageHeader.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`
**Master pattern:** "Page header — two variants" / implementation reference row *PageHeader (Ambient)*

## Purpose

The ambient band at the top of every student page. The Master: *"Two variants — hero-with-KPI and compact — same gradient formula, share one component with a 'compact' prop."* One component, not a per-page header.

## Variants

| Variant | Master usage | Treatment |
|---|---|---|
| `Hero` | Dashboard, Requests | `--motiva-gradient-ambient-hero` (the stronger wash), 40/30 block padding, quiet greeting line in ink-2, 15px supporting row |
| `Compact` (default) | Tasks, Knowledge Center | `--motiva-gradient-ambient`, 38/28 block padding, 25px/700 page title, 13.5px context line |

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Variant` | `MPageHeader.HeaderVariant` | `Compact` | |
| `Title` | `string?` | `null` | Rendered as `h1` in **both** variants |
| `Subtitle` | `string?` | `null` | Ignored when `SubtitleContent` is set |
| `SubtitleContent` | `RenderFragment?` | `null` | For the hero's two-ended supporting row |
| `ChildContent` | `RenderFragment?` | `null` | Snapshot slot — KPI row, milestone stepper |
| `Class` | `string?` | `null` | |

## Layout contract

Inline padding comes from `--motiva-content-padding-inline` (the token Phase 2 put on the student shell), **not** a local 40px. The header is meant to bleed to the shell edge while its text stays on the same left/right boundary as the content below it — pages must not wrap it in their own padded container.

## Accessibility

Both variants emit a real `h1`. In `Hero` the greeting is *styled* quieter (ink-2, 18px) because the loud element of a hero page is its snapshot content — that is a visual decision only; the document still gets a top-level heading on every page.

## Known limitations

- No trailing-action slot. The Master's headers carry no actions — the notification bell is global chrome in the sidebar (Phase 2).
- The Master draws the hero greeting at 17px, which has no token peer; it is snapped to the 18px `--motiva-font-size-h2` step rather than adding a one-off literal.
