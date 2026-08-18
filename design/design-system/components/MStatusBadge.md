# MStatusBadge

**Location:** `Client/Components/Badge/MStatusBadge.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`

## Purpose

Single shared status→color mapping, replacing ~10 independent badge implementations found by the Phase 1 audit (`sreq-status-badge`, `pm-status-badge`, `tdm-status-pill`, …), each of which computed its own color mapping from a status string. Not yet wired into any production page — screens keep their own `StatusClass()` logic until Phase 5 migrates them onto this component.

## Variants

**Canonical (System Master Phase 3):** `Neutral` (default) · `Violet` · `Teal` · `Rose`.
The Master permits exactly three semantics — violet = focus/action/in-progress, teal = progress/completion, rose = attention — and says *"never introduce a fourth."*

**Legacy (pre-Master, still supported):** `Info` · `Success` · `Warning` · `Danger`. Each pairs a `--motiva-color-*` foreground with its matching `-bg` token.

### Migration path

The legacy members were **not** removed: they are in use today across every role (Mentor, Lecturer, Staff, Admin). Phase 1 already folded their tokens onto the permitted three inside `.motiva-student` (`info`→violet, `success`→teal, `warning`/`danger`→rose), so they are *visually* correct there — only their names belong to the old system.

| Legacy | Canonical |
|---|---|
| `Info` | `Violet` |
| `Success` | `Teal` |
| `Warning` | `Rose` |
| `Danger` | `Rose` |

New Student code uses the canonical four. Existing callers convert as their page is redesigned; the legacy members are deleted only once no caller references them. The canonical members were **appended** to the enum, never inserted, so no existing member's ordinal changed.

### Student-scope alignment

`.motiva-student .m-badge` raises the pill to the Master's chip metrics (600 weight, 6px/10px padding) instead of the pre-Master 700 on 4px/8px. That rule is scoped, so Mentor / Lecturer / Staff / Admin render exactly as before — the same firewall the token scope itself uses.

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
