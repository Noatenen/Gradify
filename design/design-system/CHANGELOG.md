# Motiva Design System — Changelog

Dates are taken from Git history (`git log --date=short`) where the relevant
commit exists; all Phase 1–4.4 work landed on 2026-07-18.

## System Master Phase 1 — foundations rebase, student scope (2026-08-06)

Foundations-only rebase of the design-token layer onto the approved
**Motiva System Master**, which is now the visual source of truth (superseding
`motiva-ui-library-v01.html`). An implementation audit found three token
systems live at once: the legacy `--g-*` palette, the `--motiva-*` layer that
aliases it, and the Master's own `motiva-student.css` — which the app matched
in neither typeface (Assistant vs Heebo), scale (20px vs 13.5px body), nor hue
family (cool slate vs warm paper/ink).

- **Student-scoped, not forked.** Every Master value lives in a new
  `.motiva-student` block in `motiva-tokens.css`; the `:root` blocks are
  byte-identical to before. Re-pointing in `:root` would have restyled every
  Mentor/Lecturer/Staff/Admin surface — `--motiva-color-indigo` has 137
  references, `--motiva-shadow-sm` 72, `--motiva-gradient-signature` 56,
  `--motiva-radius-lg` 48. Same firewall pattern as `AppSideNav`'s existing
  `.snav-motiva` student-only layer.
- **One live palette preserved.** The scope overrides the `--g-*` variables
  themselves, so the ~40 `--motiva-*` tokens that alias them resolve to Master
  values automatically with no second definition to drift. Thirteen genuinely
  net-new tokens get their own literals (rose, rose-ink, the 4th ink step, the
  middle line step, edge-violet, item size, two tracking values, four
  gradients, one easing).
- **Rebased:** typeface (Heebo, Assistant retained as fallback); 4-step warm
  ink ramp; warm paper surfaces (`#FAF9F7`); 3-step line ramp; violet/teal/rose
  with info+warning folded onto the permitted three; the full type scale
  (page 25 / section 18 / card 15 / sub 13 / body 13.5 / meta 12); radii
  10/14/18/24; shadows reassigned (rest `none`, md popover, lg modal); five
  gradient definitions including both ambient intensities the Master uses; two
  easings.
- **Dark mode preserved, not redesigned.** Because `.motiva-student` sits on a
  descendant of `<html>`, its light values would shadow the `:root` dark blocks
  regardless of specificity — so every colour/shadow/gradient token is restated
  under `[data-theme="dark"] .motiva-student` and a `prefers-color-scheme`
  twin. Existing slate dark neutrals carry through unchanged; only accents get
  dark-legible variants. The Master's warm-charcoal `oklch()` dark palette is
  explicitly *not* adopted — its own audit marks it "a palette reference, not a
  second screen" — and is recorded in `MOTIVA_FOUNDATIONS.md` as an open
  decision.
- **The Master's component stylesheet was NOT ported.** No `.kpi`, `.kpi-grid`,
  `.row`, `.dot`, `.navi`, `.catcard`, `.amb`, `.amb-active` or `.t-*`; no
  `@keyframes`. Gradients are stored as token values only, with no rule
  applying them. Phase 3 builds real Blazor components rather than copying
  prototype CSS into production.
- **Zero visual change shipped.** Nothing carries `.motiva-student` yet; the
  class is attached to the student shell in Phase 2. Exactly one
  non-custom-property declaration was introduced app-wide —
  `.motiva-student { font-family: var(--motiva-font-family); }` in
  `motiva-base.css` — needed because `app.css` sets `font-family` on
  `html, body` with a literal, so re-pointing `--g-font-base` alone would never
  reach the page.
- No `.razor` or `.razor.css` file changed. No navigation, route, service,
  auth, data-model or backend change. Layout tokens (`--g-sidebar-width`,
  `--g-shell-padding`, `--motiva-space-*`) deliberately untouched — Phase 2/3.

## Phase 4.4 — MCard full-bleed padding (2026-07-18)

Architectural follow-up from migrating `ActionCenterCard` (the first Phase 5
production slice): that migration needed hand-derived padding compensation
(`24px - 8px = 16px`, etc.) on every section, plus a `border-top` divider
recreation, because `MCard.Padding` had no zero option. A review before
continuing to the next card confirmed this wasn't a one-off — three of the
four remaining `MCard`-bound dashboard cards (`UpcomingSubmissionsCard`,
`TeamTasksCard`, `StudentDashboardHero`) share the identical zero-outer-
padding, section-managed-padding, edge-to-edge-divider architecture
`ActionCenterCard` had, so the same compensation would have repeated at
least twice more with different numbers each time.

- Added `MCard.CardPadding.None` (maps to `padding: 0`) alongside the
  existing `Small`/`Medium`/`Large` — fully backward compatible, default
  stays `Medium`.
- The optional header (`Icon`/`Title`/`Subtitle`/`TrailingContent`) keeps a
  fixed, token-driven inset (`padding-inline`/`padding-block-start:
  var(--motiva-space-lg)`) even when `Padding="None"`, via a rule scoped to
  `.m-card-padding-none .m-card-header` — it can only ever match under
  `None`, so `Small`/`Medium`/`Large` are structurally incapable of
  receiving double padding from this change.
- `ActionCenterCard` migrated to `Padding="None"`; every padding
  compensation introduced during its original migration was reverted to the
  section's original, unmodified value (`.ac-item`, `.ac-empty`, and the
  responsive breakpoint all restored). The `border-top` divider recreation
  on `.ac-list`/`.ac-empty` was kept — `MCard`'s header still has no border
  of its own regardless of `Padding`, so that recreation is unrelated to
  this fix and remains structurally necessary.
- Two new gallery examples at `/motiva/components/card` under a new
  "Full-bleed body (Padding=None)" section: a body-only full-bleed example,
  and a header + full-bleed body example matching `ActionCenterCard`'s real
  shape. The existing `Medium`-padding header example was re-verified
  unaffected, not re-authored.
- `MCard.md` documents `CardPadding.None`, when to reach for it, and that
  section-level padding inside `ChildContent` remains the consumer's own
  responsibility in every `Padding` mode, `None` included.

## Phase 4.3 — MCard header extension (2026-07-18)

Pre-migration readiness fix identified during Phase 5 planning: a design-
system readiness review found the same icon+title(+trailing) header row
hand-rolled independently in the large majority of dashboard cards
(`ActionCenterCard`, `UpcomingSubmissionsCard`, `TeamTasksCard`,
`ProjectDetailsCard`, `StudentDashboardHero`), and `MCard` had no slot for
it — only bare `ChildContent`. Migrating any of those cards as-is would have
relocated that duplication inside `MCard` instead of removing it.

- Added four new optional `MCard` parameters: `Icon` (`RenderFragment?`),
  `Title` (`string?`, renders as `<h3>`), `Subtitle` (`string?`), and
  `TrailingContent` (`RenderFragment?`, pinned to the end of the header row).
  The header block only renders when at least one is set — fully backward
  compatible, verified against every existing gallery usage (none of which
  use the new parameters) with no changes required.
- Header layout uses `align-items: flex-start` so `Icon`/`TrailingContent`
  stay pinned to the first line of `Title` even when it wraps to two lines,
  and `margin-inline-start: auto` (never `left`/`right`) to pin
  `TrailingContent` to the row's end under both RTL and LTR.
- 100% token-driven, no new hardcoded values (`--motiva-font-size-h3` for
  `Title`, `--motiva-font-size-sm`/`--motiva-text-secondary` for `Subtitle`,
  `--motiva-font-size-lg` for the icon area, `--motiva-space-sm`/`-xs`/`-md`
  for gaps/margins).
- Five new gallery examples added to `/motiva/components/card` (body-only,
  icon+title, icon+title+trailing, title+subtitle, wrapping-title case) plus
  updated anatomy/accessibility/Do-Don't/limitations copy and a Razor code
  sample. `MCard.md` documents the new parameters, when to fall back to
  `ChildContent` instead, and the RTL/accessibility behavior of the header
  row.
- Deliberately out of scope (per the readiness review): no chip/count-badge
  primitive, no `MButton` icon-only mode, no `MStatusBadge` variant changes,
  no `MModal` accessibility fixes — none of those block adopting this
  extension, and none were needed to solve the specific header-duplication
  gap this phase targets.
- No production dashboard page was touched — `MCard` still has zero
  production adoption; this only prepares its API ahead of Phase 5.

## Phase 4.2 — Production polish (2026-07-18)

Final polish pass before Phase 5 (Patterns). Turns the gallery from internal
documentation into a self-contained developer experience — no new
architecture, no new design-system primitives, no component API changes.

- **Search:** a lightweight, keyboard-friendly search box in
  `GallerySidebar` filters Foundations/Components/top-level nav live as you
  type; `Enter` jumps to the first match, `Escape` clears. Pure Blazor
  state — no JavaScript added.
- **Copy experience:** new `CopyButton` gallery component reuses the
  existing `window.gradify.copyToClipboard` JS helper (already shipped in
  `gradify-interop.js` for other features) — no new JavaScript introduced.
  Wired into `CodeBlock` (copy the whole snippet) and `TokenSwatch` (copy
  the token name), each with success feedback (icon swap + `aria-live`
  announcement).
- **Related Components:** every component page now ends with a "Related
  Components" section (new `RelatedComponents` gallery component) linking
  to components that genuinely compose together elsewhere in the gallery
  (e.g. Button ↔ Card ↔ Modal, Card ↔ Status Badge ↔ Progress Bar) — no
  invented relationships.
- **Hero System Overview:** a compact stat-tile row under the hero
  reporting real, traceable numbers (5 stable components, 9 Foundations
  categories) plus status badges for Playground/RTL/Accessibility support —
  not marketing numbers.
- **Deduplication:** extracted the identical title/subtitle/purpose header
  markup+CSS that was copy-pasted across all five component pages (plus
  Playground/Changelog) into one shared `DocPageHeader` component.
- **Playground:** controls grouped into semantic `<fieldset>`/`<legend>`
  sections (Appearance / State / Content) for both visual and
  screen-reader clarity.
- **Changelog:** each entry now carries a phase icon inside its timeline
  dot (roadmap feel over plain bullet list); this entry and Phase 4.1 were
  added, with Phase 4.2 marked as the current/latest entry.
- **Accessibility pass:** `aria-live` announcement for empty search
  results, `fieldset`/`legend` semantic grouping in the Playground, focus
  order re-verified end to end.
- **Responsive audit:** reviewed at 320/375/768/1024/1440/1920px; added
  defensive `flex-wrap` on a few hero rows that could theoretically
  overflow at the narrowest widths.
- No production page, route, layout, API, or backend code was touched.

## Phase 4.1 — Visual polish (2026-07-18)

- **Hero:** rebuilt as a two-column layout — intro copy beside a composed
  "showcase" card built entirely from real `MButton`/`MStatusBadge`/
  `MProgressBar` inside an `MCard`, proving the design system rather than
  just describing it.
- **Typography:** rewritten as a scannable spec sheet (glyph + name + token
  + weight/size/line-height facts + a real example sentence per row)
  instead of a loose list of styled headings.
- **Foundations:** `TokenSwatch` now renders the token name as a chip
  instead of bare text, with a hover lift — reads as a reusable design
  asset.
- **Changelog:** rebuilt as a vertical timeline (connected dots/rail,
  highlighted current entry, phase badges + dates) instead of a flat list
  of cards.
- **Sidebar:** active nav items get a reserved accent bar
  (`border-inline-start`) that fills with brand color; hover gets a subtle
  nudge, kept visually distinct from the active state.
- **Playground:** controls panel de-chromed and narrowed; preview promoted
  to a spotlighted "stage" (ambient background, elevated shadow) via a new
  `ComponentExample Emphasis` flag.
- **Code blocks:** padding increased, long lines wrap instead of forcing
  horizontal scroll.
- Section rhythm tightened across every page (48px → 32px between
  sections) to remove unintentional empty space.
- No component APIs changed; no production components modified.

## Phase 4 — Gallery and Playground (2026-07-18)

- Added a development-only internal gallery at `/motiva`, gated to the
  Development environment via `IWebAssemblyHostEnvironment.IsDevelopment()`
  (see `GALLERY.md` for the documented limitation of that gate).
- New layout: `Client/Shared/MotivaGalleryLayout.razor` + sidebar
  (`Client/Components/MotivaGallery/GallerySidebar.razor`).
- New reusable gallery helpers: `GallerySection`, `ComponentExample`,
  `CodeBlock`, `TokenSwatch` (`Client/Components/MotivaGallery/`).
- New routed pages under `Client/Pages/Motiva/`: Getting Started
  (`/motiva`), Foundations (`/motiva/foundations`, anchored sections for
  Colors/Gradients/Typography/Spacing/Radius/Shadows/Motion/Accessibility/RTL),
  one page per component (`/motiva/components/button|card|status-badge|
  progress-bar|modal`), an interactive Playground (`/motiva/playground`),
  and this Changelog (`/motiva/changelog`).
- Every example renders the real `MButton`/`MCard`/`MStatusBadge`/
  `MProgressBar`/`MModal` production components — no re-implemented HTML
  stand-ins.
- No production page, route, layout, API, or backend code was touched. No
  domain/business patterns were introduced (Patterns/Templates remain
  "Coming later" placeholders in the sidebar).

## Phase 3.5 — Component review and stabilization (2026-07-18)

- Reviewed `MButton`, `MCard`, `MStatusBadge`, `MProgressBar`, `MModal`
  against an 18-point checklist before any production adoption existed.
- Fixed `MCard.CardVariant` conflating visual style with interaction mode:
  split out an independent `Interactive` parameter so `Variant` and
  `Interactive` can be combined freely.
- Fixed `MStatusBadge` rendering an accessibility-silent empty pill when
  neither `Text` nor `ChildContent` was supplied — it now renders nothing.
- Documented (not fixed) several nice-to-have items: `MButton.Loading`'s use
  of native `disabled` vs. `aria-disabled`, `Type` as `string` not enum,
  three near-duplicate size enums, `MCard` silently ignoring `OnClick`
  without `Interactive`, `MCard`'s Space-key scroll, `MModal`'s missing
  focus trap/body-scroll lock.
- See `COMPONENT_REVIEW.md` for the full method and checklist results.

## Phase 3 — Initial components (2026-07-18)

- Built the first five Motiva primitives under `Client/Components/`:
  `MButton`, `MCard`, `MStatusBadge`, `MProgressBar`, `MModal` — chosen by
  highest duplication count × lowest domain-specificity per the Phase 1
  audit.
- Every component consumes only `--motiva-*` tokens; the two literal-color
  exceptions (button/badge text-on-gradient, modal backdrop scrim) are
  documented in each component's own doc file under
  `design/design-system/components/`.
- No production page was migrated; adoption is explicitly deferred to a
  future "Phase 5 — Dashboard Migration."

## Phase 2 — Design tokens (2026-07-18)

- Added `Client/wwwroot/css/motiva-tokens.css` and `motiva-base.css`,
  loaded in `index.html` after `gradify-theme.css`.
- Aliased every token that already existed in `gradify-theme.css` (surfaces,
  text, borders, spacing, radius-sm/md, shadows, font family/size, danger)
  via `var(--g-*)` so there is exactly one live neutral/semantic palette and
  dark mode keeps working automatically.
- Introduced net-new tokens with no `gradify-theme.css` equivalent: the
  4-stop brand gradient, ambient background gradient, info/success/warning
  semantics, motion durations/easings, z-index layers, container widths,
  and control heights — values taken from
  `design/design-system/motiva-ui-library-v01.html`.
- See `MOTIVA_FOUNDATIONS.md` for the full alias-vs-net-new rationale and
  the consumption rules every later phase follows.

## Phase 1 — UI architecture audit (2026-07-18)

- Audited the existing Blazor WebAssembly client: no shared component
  library existed; ~15+ independent card implementations, ~10 independent
  status badge implementations, 4+ button styles, and 9+ modal/overlay
  implementations were found across feature pages.
- Recommended the first five components to build (`MCard`, `MStatusBadge`,
  `MButton`, `MModal`, `MProgressBar`) and the student dashboard as the
  first migration target for a future phase.
- No application code was changed; see `UI_ARCHITECTURE_AUDIT.md` for the
  full findings.
