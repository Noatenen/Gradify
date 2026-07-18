# Motiva Design System — Changelog

Dates are taken from Git history (`git log --date=short`) where the relevant
commit exists; all Phase 1–4.3 work landed on 2026-07-18.

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
