# MButton

**Location:** `Client/Components/Button/MButton.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components` (imported globally via `Client/_Imports.razor`, no `@using` needed per-page)

## Purpose

Single shared `<button>` primitive replacing the 4+ independent button styles found across the app (`fm-btn`, `atm-btn`, `iim-btn`, `cy-add-btn`/`cy-ghost-btn`, `spp-btn-save`, raw Bootstrap `.btn.btn-primary`, …) per the Phase 1 audit. Not yet wired into any production page (Phase 5).

## Variants

| Variant | Look |
|---|---|
| `Primary` | Filled with the Motiva signature gradient, white text |
| `Secondary` | White/surface fill, indigo text, subtle border |
| `Ghost` | Transparent, muted text, hover surface |
| `Danger` | Filled with `--motiva-color-danger`, white text |

## Sizes

`Small` (`--motiva-control-height-sm`, 32px) · `Medium` (40px, default) · `Large` (48px)

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `ChildContent` | `RenderFragment?` | — | Button label/content |
| `Variant` | `MButton.ButtonVariant` | `Primary` | `Primary` / `Secondary` / `Ghost` / `Danger` |
| `Size` | `MButton.ButtonSize` | `Medium` | `Small` / `Medium` / `Large` |
| `Disabled` | `bool` | `false` | |
| `Loading` | `bool` | `false` | Shows spinner, sets `aria-busy`, disables the button |
| `LoadingText` | `string?` | `null` | Replaces `ChildContent` while `Loading`; if omitted, `ChildContent` stays visible next to the spinner |
| `Type` | `string` | `"button"` | Native `type` attribute (`"submit"`, `"reset"`, …) |
| `OnClick` | `EventCallback<MouseEventArgs>` | — | |
| `AriaLabel` | `string?` | `null` | For icon-only buttons |
| `Class` | `string?` | `null` | Extra CSS classes appended to the root |

## Accessibility notes

- Renders a semantic `<button>` — keyboard-operable (Enter/Space) for free.
- `Loading` sets `aria-busy="true"` and the native `disabled` attribute, which both prevents duplicate activation and reports state to assistive tech.
- `disabled` is applied whenever `Disabled || Loading` is true — callers never need to compute this themselves.
- `:focus-visible` gets an explicit 2px ring using `--motiva-color-focus-ring` with `outline-offset`, since the gradient/danger fills are custom surfaces (per `MOTIVA_FOUNDATIONS.md`'s accessibility rule, the default global ring isn't relied on here).
- Disabled state uses `--motiva-color-disabled-bg/-text/-border`, never `opacity` alone.

## RTL behavior

No hardcoded `left`/`right`. Icon/spinner gap uses `gap` (logical by nature in flex). The spinner's open edge uses `border-inline-end-color`, not `border-right-color`, so it mirrors correctly under `dir="rtl"`.

## Usage examples

```razor
<MButton Variant="MButton.ButtonVariant.Primary" OnClick="SaveAsync">
    שמירה
</MButton>

<MButton Variant="MButton.ButtonVariant.Secondary" Size="MButton.ButtonSize.Small">
    ביטול
</MButton>

<MButton Variant="MButton.ButtonVariant.Danger" AriaLabel="מחיקת פרויקט">
    מחיקה
</MButton>

<MButton Loading="_isSaving" LoadingText="שומר…" OnClick="SaveAsync">
    שמירה
</MButton>
```

## Do / Don't

- **Do** pass `AriaLabel` for any button whose visible content is icon-only.
- **Do** let `MButton` compute its own disabled/loading class state — don't pass extra classes to fake a variant.
- **Don't** wrap `MButton` in another clickable element (e.g. a card `onclick`) — it already handles its own click/keyboard behavior.
- **Don't** put block-level content (headings, cards) inside `ChildContent` — it's a button, keep it to label + optional inline icon.

## Known limitations / approved exceptions

- `color: #fff` is hardcoded on `.m-btn-primary` and `.m-btn-danger` (text on a filled/gradient background). No `--motiva-*` token represents "text on brand surface" today; the visual reference (`motiva-ui-library-v01.html`) hardcodes the same literal. Documented here rather than silently deviating from the "tokens only" rule.
- No pressed/`:active` state is defined yet — only hover and focus-visible. Can be added in a later pass without changing the public API.
