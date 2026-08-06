# Motiva Design System — Foundations

**Phase:** 2 — Design Tokens (foundations only; no screens migrated, no Razor components yet)
**Files:** `Client/wwwroot/css/motiva-tokens.css`, `Client/wwwroot/css/motiva-base.css`
**Depends on:** `Client/wwwroot/css/gradify-theme.css` (existing, unmodified)
**Visual source of truth:** `design/design-system/motiva-ui-library-v01.html`

> **Superseded in part — see [Motiva System Master rebase](#motiva-system-master-rebase-student-scope) at the end of this document.**
> The approved **Motiva System Master** is now the visual source of truth. Its values live in a `.motiva-student` scope block; everything described above that section still governs the global `:root` layer and every non-student role.

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

---
---

<a id="motiva-system-master-rebase-student-scope"></a>

# Motiva System Master rebase — the `.motiva-student` scope

**Phase:** 1 of the System Master migration (foundations only)
**Visual source of truth:** the approved **Motiva System Master**, and its own token file `motiva-student.css` (in the design project — *not* copied into this repo)
**Files touched:** `motiva-tokens.css`, `motiva-base.css`, `index.html`
**Visual change shipped:** none. The scope is declared, not applied.

## Why a scope instead of `:root`

The System Master describes the **Student experience only**. It has no finalized Lecturer screens, and Mentor is delivered as a single reference file rather than standalone pages.

The tokens it redefines are, however, consumed app-wide:

| Token | References in `Client/**/*.css` |
|---|---|
| `--motiva-color-indigo` | 137 |
| `--motiva-shadow-sm` | 72 |
| `--motiva-gradient-signature` | 56 |
| `--motiva-radius-lg` | 48 |

Re-pointing any of those in `:root` would silently restyle every Mentor, Lecturer, Staff and Admin surface. So every Master value lives in a `.motiva-student` block, and the `:root` blocks are byte-identical to before.

This is the same firewall `AppSideNav` already uses for its `.snav-motiva` student-only override layer.

## Why the palette is not forked

The scope overrides the **`--g-*` variables themselves**. The ~40 `--motiva-*` tokens that alias them (surfaces, text, borders, danger, `radius-sm`/`md`, shadows, font family and sizes) therefore resolve to Master values automatically, with no second definition to drift. Only genuinely net-new concepts get their own literal:

`--motiva-text-subtle` · `--motiva-border-card` · `--motiva-color-rose` · `--motiva-color-rose-ink` · `--motiva-color-edge-violet` · `--motiva-font-size-item` · `--motiva-letter-spacing-page` · `--motiva-letter-spacing-section` · `--motiva-gradient-ambient-hero` · `--motiva-gradient-edge` · `--motiva-gradient-edge-active` · `--motiva-gradient-wash` · `--motiva-motion-easing-emphasized`

## Final token values (light)

### Typeface

`--g-font-base: Heebo, 'Assistant', 'Helvetica Neue', Helvetica, Arial, sans-serif`

Heebo is the Master's single canonical UI face. Assistant is kept as the immediate fallback so any glyph Heebo lacks resolves to today's face rather than a system serif. Both fonts load in `index.html`; Assistant remains the face for every non-student role and for the auth pages.

### Ink ramp

| Token | Value | Was | Master name |
|---|---|---|---|
| `--g-text-primary` | `#1A1820` | `#1e293b` | `--ink` |
| `--g-text-secondary` | `#5B5568` | `#64748b` | `--ink-2` |
| `--g-text-muted` | `#8B8698` | `#94a3b8` | `--ink-3` |
| `--motiva-text-subtle` | `#B7B2C2` | *(net new)* | `--ink-4` |

Warm neutral, replacing the cool slate ramp.

### Surfaces

| Token | Value | Was |
|---|---|---|
| `--g-bg-page` | `#FAF9F7` (Paper) | `#f0f2f8` |
| `--g-bg-surface` | `#FFFFFF` | `#ffffff` (unchanged) |
| `--g-bg-input` | `#FAF9F7` | `#f8fafc` |
| `--g-bg-hover` | `rgba(26,24,32,0.035)` | `#f5f6ff` |

Surface hierarchy, lowest to highest: **Paper → white card → Ambient-bordered card** (`--motiva-gradient-edge`, the highest-emphasis surface).

### Line ramp

The Master has three steps where `--g-*` had two; the middle one is added rather than overloading either end.

| Token | Value | Master name | Use |
|---|---|---|---|
| `--g-border-light` | `#F0EEF9` | `--line` | row dividers |
| `--motiva-border-card` | `#EFEDF4` | `--line-2` | card outlines |
| `--g-border` | `#E7E4EC` | `--edge` | strong edges |

### Brand and semantic colour

**The Master permits exactly three semantic colours and says never to introduce a fourth:**

| Token | Value | Meaning |
|---|---|---|
| `--motiva-color-violet` | `#4F46E5` | focus / action / primary |
| `--motiva-color-teal` | `#0D9C9A` | progress / completion / on-track |
| `--motiva-color-rose` | `#D93864` | attention only — reserved, never decorative |
| `--motiva-color-rose-ink` | `#B23256` | rose at text weight (labels), vs `rose` for dots and fills |

Supporting hues — the Master uses two different violets and they are kept distinct rather than averaged:

| Token | Value | Use |
|---|---|---|
| `--motiva-color-purple` | `#6D0EE6` | radial ambient page wash |
| `--motiva-color-edge-violet` | `#7C3AED` | 1px card-border gradient |
| `--motiva-color-indigo` | → `var(--motiva-color-violet)` | legacy alias; "violet" is the Master's word |

`--g-accent` → violet · `--g-accent-hover` `#3730A3` · `--g-accent-light` `rgba(79,70,229,.07)` · `--g-accent-active-bg` `rgba(79,70,229,.10)` · `--g-danger` → rose.

**Info and warning are folded onto the permitted three** (`info` → violet, `warning` → rose, `success` → teal) rather than deleted, so `MStatusBadge`'s existing five-variant API keeps compiling untouched while the rendered result obeys the Master's rule. Narrowing the API itself is Phase 3.

### Typography scale

The legacy scale ran roughly 1.8× oversized (body 20px, page title 32px).

| Token | Value | Master role | Weight |
|---|---|---|---|
| `--g-type-page-title` | `25px` | Page title | 700 |
| `--g-type-section` | `18px` | *derived* — modal titles | 700 |
| `--g-type-card` | `15px` | Section title | 700 |
| `--g-type-subtitle` | `13px` | Sub label | 600 |
| `--g-type-body` | `13.5px` | Body | 400 |
| `--g-type-secondary` | `12px` | Meta / caption | 400 |
| `--motiva-font-size-item` | `14px` | Item / row text *(net new)* | 500 |

Legacy aliases: `xs 12` · `sm 13` · `md 13.5` · `lg 15` · `xl 25`.
Line heights: `tight 1.2` · `heading 1.35` · `body 1.6` · `caption 1.4` (unchanged).
Tracking: `--motiva-letter-spacing-page: -0.4px` · `--motiva-letter-spacing-section: 0.02em`.

`--g-type-section` at 18px is the one **derived** value in this table — the Master's own scale has no step between 25px and 15px, but its modal headings consistently render at 18px. Flagged so it can be corrected rather than assumed authoritative.

### Radius

| Token | Value | Use |
|---|---|---|
| `--g-radius-sm` | `10px` | nav items, icon buttons |
| `--g-radius-md` / `--motiva-radius-md` | `14px` | inner elements (Master `--r-inner`) |
| `--g-radius-lg` / `--motiva-radius-lg` | `18px` | cards (Master `--r-card`) |
| `--motiva-radius-xl` | `24px` | modals only |
| `--motiva-radius-full` | `9999px` | pills and chips |

Nothing in the Master reaches the legacy `26px` / `34px`.

### Elevation

The Master: *"Cards: hairline border, no drop shadow at rest. Modals only: soft elevated shadow."*

Shadows are **reassigned, not removed** — the Master does use them, just never on a resting card:

| Token | Value | Use |
|---|---|---|
| `--g-shadow-sm` | `none` | resting surfaces |
| `--g-shadow-md` | `0 16px 36px rgba(30,20,60,.14)` | dropdowns, popovers |
| `--g-shadow-lg` | `0 32px 70px rgba(30,20,60,.28)` | modals |

### Ambient and gradient rules

Two radial washes — violet at the top reading-end, teal at the bottom reading-start — over a white→paper linear base. **One per page load, at the top only.**

The Master uses two intensities and both are recorded rather than picking one:

- `--motiva-gradient-ambient` — compact page headers (Tasks, Requests, Knowledge Center, Profile): `88% 6%` @ .20 / `4% 96%` @ .13
- `--motiva-gradient-ambient-hero` — Dashboard hero: `88% 4%` @ .24 / `4% 96%` @ .15

Edge gradients, all a 92° violet→transparent→teal sweep:

- `--motiva-gradient-edge` — the 1px Ambient card border (`.30 → 0 → .28`)
- `--motiva-gradient-edge-active` — its selected state (`.55 → .03 → .52`)
- `--motiva-gradient-wash` — the same sweep as a soft fill (`.075 → .02 → .07`), the Master's active-nav / selected-surface treatment

`--motiva-gradient-signature` collapses inside the scope from the 4-stop purple→indigo→blue→teal to the Master's own two-hue sweep — **the Master has no blue at all.** Outside the scope the original 4-stop gradient is untouched.

All five are **values only**. No rule in Phase 1 applies any of them.

### Motion

Durations (`120` / `200` / `320ms`) already matched the Master and are unchanged. Easing:

- `--motiva-motion-easing-standard: ease-out` — the Master's transitions are `ease-out` throughout
- `--motiva-motion-easing-emphasized: cubic-bezier(0.2, 0.8, 0.2, 1)` *(net new)* — its modal entrance curve

Reduced-motion handling in `motiva-base.css` is unchanged and still applies globally with no per-component opt-in.

## Dark mode

`.motiva-student` sits on a **descendant** of `<html>`, and a custom property resolves from the nearest declaring ancestor — so the light values would shadow the `:root`-level dark blocks regardless of specificity. Dark equivalents are therefore mandatory, and every colour, shadow and gradient token set in the light scope is restated in:

```
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) .motiva-student { … } }
[data-theme="dark"] .motiva-student { … }
```

Non-colour tokens (typeface, type scale, radii, tracking, motion) are **not** restated — those selectors match the same element, so anything not redeclared falls through by normal cascade.

**Dark mode is preserved, not redesigned.** The existing slate dark neutrals are carried through unchanged; only values whose *light* counterpart changed receive a dark equivalent, with the accents shifted to their dark-mode-legible variants (violet `#818cf8`, teal `#2dd4c4`, rose `#f06a90`).

### Open decision — the warm-charcoal dark palette

The Master's Profile screen ships a full warm-charcoal dark palette in `oklch()`. Its own audit marks that as **"BY DESIGN … a palette reference, not a second screen — the light shell is the page to implement."** It is therefore *not* adopted as spec here. Recorded for a future decision:

```
page       oklch(0.29 0.032 322)      ink     oklch(0.93 0.018 75)
sidebar    oklch(0.27 0.030 320)      ink-2   oklch(0.88 0.015 60)
card       oklch(0.35 0.028 316)      ink-3   oklch(0.68 0.028 300)
elevated   oklch(0.40 0.026 312)      border  rgba(255,255,255,0.06)
violet     #9C8FD6                    teal    #4FA79E
```

## What Phase 1 deliberately did **not** do

- **No component classes.** The Master's component stylesheet (`.kpi`, `.kpi-grid`, `.row`, `.dot`, `.navi`, `.catcard`, `.amb`, `.amb-active`, `.t-*`) was **not** ported. Phase 3 builds real Blazor components instead of copying prototype CSS into production.
- **No `@keyframes`.** `motivaBreathe`, `motivaFadeUp` and `motivaModalIn` ship in Phase 3 with the components that use them.
- **No layout tokens.** `--g-sidebar-width` (280→248), `--g-shell-padding`, content padding (40px / 60px) and `--motiva-space-*` are untouched — shell geometry is Phase 2, component padding rhythm is Phase 3.
- **No `.razor` or `.razor.css` changes**, and no route, navigation, service, auth or backend changes.

## Activation

Exactly one non-custom-property declaration is introduced app-wide, in `motiva-base.css`:

```css
.motiva-student { font-family: var(--motiva-font-family); }
```

This is required because `app.css` sets `font-family` on `html, body` with a **literal** (`'Assistant'`), not a token — descendants inherit that literal, so re-pointing `--g-font-base` alone would never reach the page.

Nothing in the app carries `.motiva-student`. Attaching it to the student shell is the first task of Phase 2, and that single class is what switches the entire foundations rebase on — for the Student experience and no other role.
