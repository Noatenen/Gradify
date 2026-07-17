# Motiva Design System — Foundations

**Phase:** 2 — Design Tokens (foundations only; no screens migrated, no Razor components yet)
**Files:** `Client/wwwroot/css/motiva-tokens.css`, `Client/wwwroot/css/motiva-base.css`
**Depends on:** `Client/wwwroot/css/gradify-theme.css` (existing, unmodified)
**Visual source of truth:** `design/design-system/motiva-ui-library-v01.html`

This document explains what the two new files are, why they're structured the way they are, and — most importantly — the rules future components (Phase 3+) must follow when consuming them.

---

## Why a new file instead of editing `gradify-theme.css`

`gradify-theme.css`'s `--g-*` tokens are referenced **4,453 times across 75 of 81** component stylesheets in this app. That is a live, load-bearing system — renaming or restructuring it would force touching nearly every screen, which is explicitly out of scope for a foundations-only phase.

At the same time, the Motiva visual reference (`motiva-ui-library-v01.html`) defines things `gradify-theme.css` doesn't have at all: a 4-stop brand gradient (purple → indigo → blue → teal), ambient background gradients, and full info/success/warning semantic colors (today there's only a single `--g-accent` and `--g-danger`).

So `motiva-tokens.css` does two different things depending on the token:

1. **Alias** — where `gradify-theme.css` already defines the concept (surfaces, text, borders, spacing, radius-sm/md, shadows, font family/size, danger), the `--motiva-*` variable forwards to the existing `--g-*` variable via `var(...)`. No new literal color/number is introduced, dark mode keeps working for free (it flows through `gradify-theme.css`'s existing dark-mode block), and the two files can never silently drift apart.
2. **Net new** — where nothing exists yet (brand palette, gradient, ambient, info/success/warning, motion, z-index, control heights, container widths, line-heights, font-weights), the token is defined directly from `motiva-ui-library-v01.html`'s literal values.

**There is exactly one live neutral/semantic palette in production** (`gradify-theme.css`'s). `motiva-tokens.css` is a superset namespace on top of it, not a competing one.

---

## Token categories

| Prefix | Category | Alias or net new |
|---|---|---|
| `--motiva-color-purple/indigo/blue/teal` | Brand hues | Net new |
| `--motiva-gradient-signature` | Brand gradient | Net new |
| `--motiva-gradient-ambient` | Ambient background wash | Net new |
| `--motiva-surface-*` | Surface hierarchy | Aliased |
| `--motiva-text-*` | Text color hierarchy | Aliased |
| `--motiva-border-subtle/strong` | Borders | Aliased |
| `--motiva-color-info/success/warning` | Semantic status | Net new |
| `--motiva-color-danger` | Semantic status | Aliased |
| `--motiva-color-disabled-*` | Disabled state | Derived from existing neutrals |
| `--motiva-color-focus-ring` | Focus state | Aliased (`--g-accent`) |
| `--motiva-space-*` | Spacing scale | Aliased (xs–xl); `2xl` net new |
| `--motiva-radius-sm/md/full` | Corner radius | Aliased |
| `--motiva-radius-lg/xl` | Corner radius | Net new |
| `--motiva-shadow-sm/md/lg` | Elevation | Aliased |
| `--motiva-container-*` | Max-width breakpoints | Net new |
| `--motiva-control-height-*` | Input/button heights | Net new |
| `--motiva-font-family` | Typeface | Aliased (`--g-font-base`, Assistant) |
| `--motiva-font-size-*` | Size scale | Aliased |
| `--motiva-font-weight-*` | Weight scale | Net new |
| `--motiva-line-height-*` | Line-height scale | Net new |
| `--motiva-motion-duration-*` / `-easing-*` | Motion | Net new |
| `--motiva-z-*` | Stacking layers | Net new |

## Naming rule

`--motiva-<category>-<name>`, lowercase, hyphen-separated. Never abbreviate the category (`surface`, not `srf`). This mirrors the convention already used by `gradify-theme.css` (`--g-<category>-<name>`) so both systems read the same way side by side.

## Surface hierarchy

From lowest to highest in the visual stack:

1. `--motiva-surface-app-bg` — the page background, behind everything (`.app-shell` in `AppLayout.razor.css` already paints this; `motiva-base.css` also sets it on `body` as a fallback for chrome-less layouts).
2. `--motiva-surface-primary` — the default card/panel surface.
3. `--motiva-surface-secondary` — recessed surfaces: inputs, wells, code blocks.
4. `--motiva-surface-elevated` — same fill as `primary`; elevation is expressed through `--motiva-shadow-*`, not a different color. Don't invent a darker/lighter fill for "elevated" — raise the shadow token instead.
5. `--motiva-surface-hover` — hover/pressed feedback background, layered on top of any of the above.

## Gradient usage

`--motiva-gradient-signature` is the brand identity mark — it exists to be **rare and specific**, not a general-purpose background.

**Use it for:**
- The primary CTA button treatment (see `motiva-ui-library-v01.html`'s `.btn.primary`).
- Hero/ambient bands on a page (paired with `--motiva-gradient-ambient` for the softer wash behind hero content).
- A single signature accent element per screen (e.g., a progress fill, a logo mark).

**Do not use it for:**
- Body text, borders, or anything that needs to stay legible/readable at small sizes.
- Repeated per-row elements (table rows, list items) — it will read as noise, not identity, if it appears more than once per screen.
- Any surface that already carries a status color (info/success/warning/danger) — semantic color communicates state; the gradient communicates brand. Don't make one element try to do both.
- Disabled states — disabled always uses `--motiva-color-disabled-*`, never the gradient dimmed.

## Accessibility expectations

- All new interactive elements must be reachable via `:focus-visible` — `motiva-base.css` provides a default ring (`--motiva-color-focus-ring`) for anything that doesn't define its own, but components with a custom surface (e.g., a gradient button) must ensure the ring is still visible against their own background (add `outline-offset` or a contrasting inner ring if needed — don't suppress the ring).
- Disabled controls must use `--motiva-color-disabled-text` on `--motiva-color-disabled-bg`/`-border` — never gray-out via `opacity` alone on a component that has custom colors, since opacity can wash out below WCAG contrast on some backgrounds.
- Status/semantic colors (`info`/`success`/`warning`/`danger`) must always pair a color with a non-color signal already present in this codebase's pattern (icon or label text) — never color alone. This matches existing usage (e.g. status badges already pair color with a Hebrew label).

## RTL expectations

- `direction: rtl` and `text-align: right` are owned by `app.css` on `html, body` — **do not redeclare them** in any Motiva file or component. `motiva-base.css` explicitly leaves this alone.
- Any new component CSS must use logical properties/directions instead of hardcoded `left`/`right` (`margin-inline-start`, `inset-inline-end`, `border-inline-start`, `text-align: start`) so it keeps working correctly under RTL without a separate RTL override block.
- The ambient gradient and signature gradient are defined with directional angles/positions (`90deg`, `circle at 80% 15%`) that were authored and visually approved in the RTL reference file — don't mirror or flip them for RTL; they're decorative, not directional content.

## Reduced-motion behavior

`motiva-base.css` collapses all animation/transition durations to `0.01ms` under `prefers-reduced-motion: reduce`, globally, with no per-component opt-in required. Any future component using `--motiva-motion-duration-*`/`-easing-*` gets this for free and must not re-implement its own reduced-motion handling.

## How future components must consume tokens (Phase 3+ rule)

1. **Always reference a `--motiva-*` variable — never a raw hex/px/ms value, and never a `--g-*` variable directly.** New components are part of the Motiva system; they consume its namespace even when that namespace is just forwarding to `--g-*` today. This is what lets a future palette change happen in one file instead of N components.
2. **Never hardcode color/spacing/radius/shadow/z-index/duration values** in a component's `.razor.css`. If a value you need doesn't have a token yet, add it to `motiva-tokens.css` (following the alias-first rule above) rather than inlining it.
3. **No inline `style="..."` attributes** for anything token-related. Dynamic values (e.g., a progress-bar's `width: N%`) are the one legitimate exception — they're data, not design tokens.
4. **Don't reintroduce feature-local CSS prefixes** (`spp-`, `psc-`, `tdm-`, etc.) for new shared components — that pattern is exactly what the Phase 1 audit flagged as the app's main duplication problem. Shared components get one name, not a per-screen namespace.
5. **z-index must come from `--motiva-z-*`**, never a raw number — this is the only thing standing between a future dropdown and a future modal fighting over stacking order.

---

## Load order (as wired in `Client/wwwroot/index.html`)

```
bootstrap.min.css        — grid/utilities, Reboot (box-sizing reset)
app.css                  — RTL base, font-family, Blazor scaffold rules
gradify-theme.css        — existing --g-* tokens (source of truth for aliases above)
motiva-tokens.css        — NEW — --motiva-* tokens (this phase)
motiva-base.css          — NEW — global base rules consuming --motiva-* tokens
forms-management.css     — page-specific, unchanged
airtable-management.css  — page-specific, unchanged
AuthWithAdmin.Client.styles.css — Blazor's generated CSS-isolation bundle (all *.razor.css)
```

Nothing above `motiva-*.css` in this list was modified. Nothing below it is affected, since the two new files declare only `--motiva-*` custom properties (no name collisions possible) plus a handful of low-specificity fallback rules (`body`, bare `:disabled`, `:focus-visible`) that existing page/component CSS already outranks wherever it defines its own.
