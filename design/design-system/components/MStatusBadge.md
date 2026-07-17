# MStatusBadge

**Location:** `Client/Components/Badge/MStatusBadge.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`

## Purpose

Single shared status→color mapping, replacing ~10 independent badge implementations found by the Phase 1 audit (`sreq-status-badge`, `pm-status-badge`, `tdm-status-pill`, …), each of which computed its own color mapping from a status string. Not yet wired into any production page — screens keep their own `StatusClass()` logic until Phase 5 migrates them onto this component.

## Variants

`Neutral` (default) · `Info` · `Success` · `Warning` · `Danger` — each pairs a `--motiva-color-*` foreground with its matching `-bg` token (e.g. `--motiva-color-success` on `--motiva-color-success-bg`).

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` | Takes priority over `Text` if both are set |
| `Text` | `string?` | `null` | Simple string form, used when `ChildContent` is not provided |
| `Variant` | `MStatusBadge.BadgeVariant` | `Neutral` | |
| `Icon` | `string?` | `null` | Open-iconic modifier class, e.g. `"oi-check"` — the `oi` base class is added automatically. Matches the `oi @item.Icon` pattern already used in `AppSideNav.razor` / `NotificationBell.razor`. |
| `Class` | `string?` | `null` | |

## Accessibility notes

- Status is always communicated by **text** (`Text`/`ChildContent` is the only content path — there is no color-only rendering mode), per `MOTIVA_FOUNDATIONS.md`'s rule that semantic color must always pair with a non-color signal.
- **If neither `Text` nor `ChildContent` is set, the component renders nothing** (as of the Phase 3.5 review) rather than an empty colored pill with no accessible name — this was a real gap found during review (a caller mistake used to silently produce a visually-empty badge).
- `Icon`, when present, is `aria-hidden="true"` — it's decorative reinforcement, not the sole carrier of meaning.
- Foreground/background pairs are the same pairs already defined in `motiva-tokens.css` (`--motiva-color-info` on `--motiva-color-info-bg`, etc.), which were authored together for contrast — MStatusBadge does not invent new pairings.

## RTL behavior

`display: inline-flex` with `gap` for the icon/text spacing — no hardcoded `margin-left`/`margin-right`, so icon-before-text vs. text-before-icon ordering follows normal RTL inline flow automatically.

## Usage examples

```razor
<MStatusBadge Variant="MStatusBadge.BadgeVariant.Success" Text="אושר" />

<MStatusBadge Variant="MStatusBadge.BadgeVariant.Danger" Icon="oi-warning">
    דורש טיפול
</MStatusBadge>

<MStatusBadge Variant="MStatusBadge.BadgeVariant.Info" Text="@statusLabel" />
```

## Do / Don't

- **Do** pick the variant that matches the *semantic* meaning of the status (e.g. "late" → `Danger`), not just whichever color looks closest to what a screen currently uses — the whole point of this component is one consistent status→color mapping app-wide.
- **Don't** pass only an `Icon` with empty `Text`/`ChildContent` — the component now renders nothing at all in that case (see Accessibility notes); if you truly need icon-only, use `MButton` with `AriaLabel` instead, badges are not built for that.
- **Don't** re-derive per-screen badge colors once this component is adopted (Phase 5) — route the status string through a single label/variant mapping function per domain instead of hand-picking a `Variant` ad hoc at each call site.

## Known limitations / approved exceptions

- No inline styles or hardcoded colors — all five variants resolve entirely through existing `--motiva-color-*`/`-bg` tokens.
- The component does not itself map arbitrary domain status strings (e.g. `"PendingReview"`) to a `Variant` — that mapping stays in each feature's existing label-helper classes (e.g. `MentorInvolvementLevels.Label`), consistent with "Reuse existing services/DTOs" — MStatusBadge only owns the *visual* mapping from `Variant` to color.
