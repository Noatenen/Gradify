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
| `ChildContent` | `RenderFragment?` | — | Card body |
| `Variant` | `MCard.CardVariant` | `Default` | `Default` / `Elevated` / `Ambient` — visual style only |
| `Interactive` | `bool` | `false` | Makes the whole card a click/keyboard target, independent of `Variant` |
| `Padding` | `MCard.CardPadding` | `Medium` | `Small` / `Medium` / `Large` — maps to `--motiva-space-sm/lg/xl` |
| `Class` | `string?` | `null` | |
| `OnClick` | `EventCallback<MouseEventArgs>` | — | Only wired when `Interactive="true"` |
| `Icon` | `RenderFragment?` | — | Optional leading icon area in the header row. Caller-supplied markup (e.g. an `oi` glyph span) — MCard only sizes/positions it, no forced icon font or circle/badge treatment |
| `Title` | `string?` | `null` | Optional header title. Renders as `<h3 class="m-card-title">` |
| `Subtitle` | `string?` | `null` | Optional header subtitle, shown under `Title` in a muted, smaller style |
| `TrailingContent` | `RenderFragment?` | — | Optional compact trailing header content (count badge, small action button, …). Pinned to the end of the header row |

## Header (optional)

`Icon`/`Title`/`Subtitle`/`TrailingContent` compose an optional header row that renders above `ChildContent`. It only appears when at least one of the four is set — a plain `<MCard>` with none of them renders exactly as before this addition (verified: existing gallery examples using only `Variant`/`Padding`/`Interactive`/`OnClick`/`ChildContent` are unmodified and unaffected).

This exists because the Phase 1 audit found the same icon+title(+trailing) header row hand-rolled independently in the large majority of dashboard cards (`ActionCenterCard`, `UpcomingSubmissionsCard`, `TeamTasksCard`, `ProjectDetailsCard`, `StudentDashboardHero`'s journey panel) — without it, migrating those cards to `MCard` would just relocate that duplication inside `ChildContent` instead of removing it.

```razor
<MCard Title="הגשות קרובות">
    <Icon><span class="oi oi-calendar" aria-hidden="true"></span></Icon>
    <TrailingContent><MStatusBadge Text="3" Variant="MStatusBadge.BadgeVariant.Neutral" /></TrailingContent>
    <ChildContent>
        <p>...</p>
    </ChildContent>
</MCard>
```

`Icon`/`TrailingContent` are `RenderFragment` parameters, set via nested named tags (`<Icon>…</Icon>`); `Title`/`Subtitle` are plain strings, set as attributes on `<MCard>` itself. When using any named tag (`<Icon>`/`<TrailingContent>`), wrap the remaining body markup in an explicit `<ChildContent>…</ChildContent>` tag rather than leaving it bare, to avoid ambiguity about which region it belongs to.

### When to keep custom content inside `ChildContent` instead

- **A different heading level is required.** `Title` always renders as `<h3>`. If a card's position in the page's document outline calls for `<h2>` or `<h4>`, skip `Title` and put your own heading in `ChildContent` — matches the pre-existing "MCard has no opinion on heading level in `ChildContent`" rule.
- **The header needs more than one trailing element competing for space**, or a layout `TrailingContent`'s single flex slot doesn't fit (e.g. two independent action buttons with their own spacing rules) — compose it by hand in `ChildContent` instead of fighting the slot.
- **The "icon" is actually a colored circle/badge wrapper** (e.g. `ProjectDetailsCard`'s `pdc-icon` circle vs. `ActionCenterCard`'s bare glyph) — both are supported today, since `Icon` accepts arbitrary markup: pass the whole wrapped-circle markup as the `Icon` fragment. MCard does not impose one treatment over the other.

## Accessibility notes

- When `Interactive="true"`, the root renders as `<div role="button" tabindex="0">` with both `@onclick` and `@onkeydown` (Enter/Space) wired to `OnClick`, and a visible `:focus-visible` ring. This mirrors the div-as-button keyboard pattern already used elsewhere in this codebase (`TaskMilestoneAccordion.razor`, `TeamTasksCard.razor`) rather than inventing a new one.
- When `Interactive="false"` (default), `OnClick` is not wired and the card renders a plain, non-interactive `<div>` — per the Phase 3 requirement, MCard never renders a clickable non-semantic div without keyboard support. Passing `OnClick` without `Interactive="true"` is a caller mistake; the callback is silently not invoked (documented under Do/Don't below).
- Heading levels inside `ChildContent` are the caller's responsibility (MCard has no opinion on `h2` vs `h3`); `Title` fixes the level at `h3` when used (see "When to keep custom content inside `ChildContent` instead" above).
- `Icon` is always marked `aria-hidden` by convention in every example in this doc/gallery (matching the existing `oi` icon usage pattern app-wide) — MCard doesn't enforce this itself since it never inspects the fragment's content, so the caller is responsible for it, same as any other icon usage in this codebase.
- The header row carries no extra ARIA role of its own — `Title`'s `<h3>` already gives it a discoverable landmark for screen-reader "jump to heading" navigation; no `role="heading"`/`aria-level` workaround was needed.

## RTL behavior

No hardcoded `left`/`right` anywhere in the stylesheet; padding uses the logical `padding` shorthand (already direction-agnostic) and the ambient gradient positions are intentionally left as authored in the RTL-approved reference (per `MOTIVA_FOUNDATIONS.md`, they're decorative, not directional).

The header row is built the same way: `gap` (logical by nature in flex) between `Icon`/text/`TrailingContent`, and `margin-inline-start: auto` — never `margin-left` — to pin `TrailingContent` to the row's end. Under `dir="rtl"` (set globally on `html`/`body` by `app.css`), a `flex-direction: row` container reverses its visual order automatically, so `Icon` lands on the right and `TrailingContent` on the left with zero direction-specific code.

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

<!-- Header slot -->
<MCard Title="הגשות קרובות">
    <Icon><span class="oi oi-calendar" aria-hidden="true"></span></Icon>
    <ChildContent>
        <p>...</p>
    </ChildContent>
</MCard>

<MCard Title="דורש התייחסות">
    <Icon><span class="oi oi-bell" aria-hidden="true"></span></Icon>
    <TrailingContent><MStatusBadge Text="3" Variant="MStatusBadge.BadgeVariant.Neutral" /></TrailingContent>
    <ChildContent>
        <p>...</p>
    </ChildContent>
</MCard>

<MCard Title="פרטי הפרויקט" Subtitle="פרויקט 214 – מערכת ניהול מלאי">
    <p>...</p>
</MCard>
```

## Do / Don't

- **Do** set `Interactive="true"` whenever the whole card is meant to be clickable — don't put a nested `<button>`/`<a>` that duplicates the same action as the card itself.
- **Do** reserve `Ambient` for a single hero/summary element per screen, per the foundations gradient-usage rule — not for repeated list/row cards.
- **Do** use `Title`/`Subtitle` (strings) for plain-text headers — reach for a custom heading in `ChildContent` only when the header truly needs something the slot can't express (see above).
- **Don't** pass `OnClick` without also setting `Interactive="true"` — the callback is silently ignored otherwise (no compile-time or run-time warning), matching the Phase 3.5 review's documented API decision.
- **Don't** nest another interactive `MCard` or a focusable control that itself needs the whole-card click behavior — pick one interactive target per card to avoid overlapping hit areas/keyboard traps.
- **Don't** put a primary call-to-action in `TrailingContent` — it's sized and positioned for compact content (a badge, a small secondary button), not for the card's main action.

## Known limitations / approved exceptions

- No inline styles or new hardcoded values are used; every visual property is a `--motiva-*` token.
- `Interactive`'s keydown handler does not call `preventDefault()` on Space, so a focused card may also scroll the page slightly on Space press — this matches the existing, already-shipped pattern in `TaskMilestoneAccordion.razor` and was not solved there either. Flagged here rather than silently fixed with new behavior, since a real fix would require `@onkeydown:preventDefault` scoped per-key, which Blazor doesn't support cleanly without also breaking Tab navigation on the same element.
- The header row only supports one `TrailingContent` slot — if a card needs two independent trailing elements (e.g. a badge *and* a button, each with its own spacing rule), compose them by hand in `ChildContent` instead; the slot was sized for the single-element case found in every dashboard card surveyed for this extension.
- `Title` is fixed at `h3`; there is no `TitleTag`/heading-level parameter. This was a deliberate choice to keep the API small — see "When to keep custom content inside `ChildContent` instead" above for the escape hatch.
