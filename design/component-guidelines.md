# Motiva Component Guidelines

Concrete, implementation-level rules per component category. Pairs with `design-tokens.css` (the variables referenced below) and `motiva-design-system.md` (the reasoning). Written for Blazor `.razor` + scoped `.razor.css` components, matching how this codebase is already structured.

---

## Cards

Four surfaces, four jobs — pick the one that matches what the card is for, don't default to the same treatment everywhere:

| Style | Background | Border/Shadow | Use for |
|---|---|---|---|
| Flat | `--g-bg-surface` | `--g-border`, no shadow | Reference & secondary lists |
| Elevated | `--g-bg-surface` | `--g-shadow-sm` or `--g-shadow-md` | Actionable cards — tasks, rows you click |
| Gradient wash | `linear-gradient(150deg, --g-accent-light 0%, --g-bg-surface 65%)` | soft border | Hero & context zones |
| Signature | brand gradient background, white text | `--g-shadow-glow` | The ONE current-focus card per screen (e.g. current stage) — never more than one per page |

Radius: `--g-radius-md` for standard cards, `--g-radius-lg` for larger content panels, `--g-radius-xl` reserved for the signature card only.

## Status badges

Pill-shaped (`--g-radius-full`), always icon/shape + color + text together — never color alone:

| Status | Background | Text color |
|---|---|---|
| Success / done | `--g-success-bg` | `--g-success` |
| Waiting / info | `--g-info-bg` | `--g-info` |
| Needs attention | `--g-attention-bg` | `--g-attention` |
| Destructive/error | `--g-danger-bg` | `--g-danger` |

Font size `--g-type-micro`, weight 700, padding `4-5px 10-12px`. Before adding a new status color, check whether an existing one already fits — don't invent a fifth hue.

## Buttons

Three tiers, not more:

- **Primary** — solid `--g-accent` fill, white text, `--g-radius-sm`, subtle tinted shadow. One per view for the single most important action.
- **Ghost** — transparent background, `--g-border` outline, `--g-text-secondary` text. Secondary actions.
- **Text** — no background/border, `--g-accent` text, used for low-emphasis navigation ("כל המשימות ←") not real actions.

Font size `--g-type-secondary` (15px), weight 700, padding `~10-12px 20-22px`. Hover: darken toward `--g-accent-hover`, transition 150ms.

## Inputs

Not yet demonstrated in the concept board, but should follow the same language:

- Background `--g-bg-surface`, border `--g-border`, radius `--g-radius-sm`.
- Focus state: border → `--g-accent`, add a soft ring (`0 0 0 3px` of `--g-accent-light`).
- Label above the field, `--g-type-secondary`, weight 600, `--g-text-secondary`.
- Error state: border → `--g-danger`, helper text below in `--g-danger`, `--g-type-secondary`. Never rely on the border color change alone — always pair with message text.

## Empty states

Not filler — the state a student sees most often deserves the same craft as a populated one:

- Icon in a circular tinted background (`--g-success-bg` for "all clear" positive states, `--g-bg-page`/`--g-border` for neutral "not configured yet" states).
- Headline (`--g-type-body`, weight 800) + one supporting sentence (`--g-type-secondary`, muted).
- A positive empty state ("all clear") should read as a small win, not a blank box. A neutral empty state ("not configured yet") should read as anticipation, not an error — never phrase it apologetically.

## Progress indicators

Two shapes, used consistently:

- **Linear bar** — `--g-radius-full` track in `--g-bg-input` or a tinted low-opacity accent, gradient fill (`--g-brand-blue` → `--g-brand-turquoise`) for the main progress direction. Animate width changes ~400-500ms ease.
- **Arc/ring** — for a single stat (percentage, current stage). The current-stage ring specifically uses the full brand conic gradient (`--g-brand-gradient-arc`) with a slow ambient rotation (~5-6s) — this is the one place the full three-color sweep appears as motion, not just a static fill. Other progress rings (e.g. a context card's completion stat) use a single-color arc (`--g-success` or `--g-info`), not the full gradient — reserve the multi-color sweep for the signature moment.

Always pair the visual with a number/fraction (`3/6 אבני דרך`) — the shape alone isn't enough.

## Navigation rules

- Sidebar nav items: `--g-type-body` size, weight 600, `--g-radius-sm` per row, `--g-nav-item-height` fixed height.
- Active item: subtle gradient-tinted background wash (indigo → turquoise at ~8-10% opacity) + `--g-accent` text — not a solid fill block.
- Set `direction: rtl` explicitly on the sidebar's flex container. Don't rely on inheriting it from `<html dir="rtl">` — that doesn't reliably reach flex ordering (see design-system doc § Accessibility).
- Only add a nav entry once its destination page actually exists — no links to routes that 404.

## Page hierarchy

Every primary page, checked against this before shipping:

1. **Hero/Context zone** — gradient-wash background, states "where am I" + the single most important thing to do. Quiet, not shouting.
2. **Primary content** — exactly one element with full visual weight (elevated or signature card). This is what the One Focus Rule means concretely.
3. **Secondary/reference content** — flat, quiet, collapsed-by-default if it's a full catalogue rather than daily-priority information.

If a page has two elements both demanding full attention, that's a hierarchy bug — fix it before shipping, don't ship two heroes.

## Motion guidelines

- Functional transitions (expand/collapse, status change, progress fill): 150–250ms ease. Never instant, never bouncy.
- Ambient/decorative motion (the signature ring's rotation): slow, 5-6s, continuous, low-key — meant to read as "alive," not to draw the eye away from content.
- Always wrap ambient animation in `@media (prefers-reduced-motion: reduce) { animation: none; }`. Functional transitions can stay (they're not the kind of motion that setting is meant to suppress), but nothing should be lost functionally when motion is disabled.

## Accessibility considerations

- Status = shape + color + text, always together, never color alone.
- Text contrast: verify AA against both `--g-bg-surface` and `--g-bg-page` when introducing a new text color.
- Every clickable/focusable element needs a visible focus ring using `--g-accent`.
- Icon-only buttons need an `aria-label` or equivalent accessible name.
- RTL: set `direction: rtl` explicitly wherever flex/grid ordering matters — don't assume inheritance is enough.
