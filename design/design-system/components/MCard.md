# MCard

**Location:** `Client/Components/Card/MCard.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`

## Purpose

Single shared surface primitive replacing the ~15+ independent card-shell reimplementations found by the Phase 1 audit (`spp-card`, `im-card`, `psc-card`, `pov-kpi-card`, …). Highest-leverage component in the first five since nearly every screen is card-based. Not yet wired into any production page.

## Variants

| Variant | Look |
|---|---|
| `Default` | Standard surface, subtle border, soft shadow |
| `Elevated` | Same surface, raised shadow (`--motiva-shadow-md`) |
| `Ambient` | Background is `--motiva-gradient-ambient` — reserved for hero/summary cards, kept subtle per `MOTIVA_FOUNDATIONS.md` |

`Variant` is purely visual. Whether the card is clickable is a separate, orthogonal `Interactive` flag (see below) — as of the Phase 3.5 review, an `Interactive` **variant** no longer exists so that visual style and interaction mode can be combined freely (e.g. an `Ambient` hero card that is also clickable).

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `ChildContent` | `RenderFragment?` | — | |
| `Variant` | `MCard.CardVariant` | `Default` | `Default` / `Elevated` / `Ambient` — visual style only |
| `Interactive` | `bool` | `false` | Makes the whole card a click/keyboard target, independent of `Variant` |
| `Padding` | `MCard.CardPadding` | `Medium` | `Small` / `Medium` / `Large` — maps to `--motiva-space-sm/lg/xl` |
| `Class` | `string?` | `null` | |
| `OnClick` | `EventCallback<MouseEventArgs>` | — | Only wired when `Interactive="true"` |

## Accessibility notes

- When `Interactive="true"`, the root renders as `<div role="button" tabindex="0">` with both `@onclick` and `@onkeydown` (Enter/Space) wired to `OnClick`, and a visible `:focus-visible` ring. This mirrors the div-as-button keyboard pattern already used elsewhere in this codebase (`TaskMilestoneAccordion.razor`, `TeamTasksCard.razor`) rather than inventing a new one.
- When `Interactive="false"` (default), `OnClick` is not wired and the card renders a plain, non-interactive `<div>` — per the Phase 3 requirement, MCard never renders a clickable non-semantic div without keyboard support. Passing `OnClick` without `Interactive="true"` is a caller mistake; the callback is silently not invoked (documented under Do/Don't below).
- Heading levels inside `ChildContent` are the caller's responsibility (MCard has no opinion on `h2` vs `h3`).

## RTL behavior

No hardcoded `left`/`right` anywhere in the stylesheet; padding uses the logical `padding` shorthand (already direction-agnostic) and the ambient gradient positions are intentionally left as authored in the RTL-approved reference (per `MOTIVA_FOUNDATIONS.md`, they're decorative, not directional).

## Usage examples

```razor
<MCard Variant="MCard.CardVariant.Default">
    <h3>סיכום פרויקט</h3>
    <p>נותרו 2 מתוך 6 תוצרים.</p>
</MCard>

<MCard Variant="MCard.CardVariant.Ambient" Padding="MCard.CardPadding.Large">
    <span>Eyebrow</span>
    <h2>כותרת Hero</h2>
</MCard>

<MCard Interactive="true" OnClick="OpenTaskDetail">
    <h3>פתיחת המשימה</h3>
</MCard>

<MCard Variant="MCard.CardVariant.Ambient" Interactive="true" OnClick="OpenTaskDetail">
    <h3>כרטיס Hero לחיץ</h3>
</MCard>
```

## Do / Don't

- **Do** set `Interactive="true"` whenever the whole card is meant to be clickable — don't put a nested `<button>`/`<a>` that duplicates the same action as the card itself.
- **Do** reserve `Ambient` for a single hero/summary element per screen, per the foundations gradient-usage rule — not for repeated list/row cards.
- **Don't** pass `OnClick` without also setting `Interactive="true"` — the callback is silently ignored otherwise (no compile-time or run-time warning), matching the Phase 3.5 review's documented API decision.
- **Don't** nest another interactive `MCard` or a focusable control that itself needs the whole-card click behavior — pick one interactive target per card to avoid overlapping hit areas/keyboard traps.

## Known limitations / approved exceptions

- No inline styles or new hardcoded values are used; every visual property is a `--motiva-*` token.
- `Interactive`'s keydown handler does not call `preventDefault()` on Space, so a focused card may also scroll the page slightly on Space press — this matches the existing, already-shipped pattern in `TaskMilestoneAccordion.razor` and was not solved there either. Flagged here rather than silently fixed with new behavior, since a real fix would require `@onkeydown:preventDefault` scoped per-key, which Blazor doesn't support cleanly without also breaking Tab navigation on the same element.
