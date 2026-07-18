# Motiva Design System — Gallery (Phase 4)

**Phase:** 4 — Gallery and Playground (development-only mini Storybook)
**Branch:** `feature/motiva-design-system`
**Scope:** A new, internal-only route rendering the actual Phase 2–3.5
foundations/components for visual QA. No production page, layout, route,
API, or backend code was changed. No domain/business patterns were added.

---

## Route

**`/motiva`** — plus nested routes:

| Route | Content |
|---|---|
| `/motiva` | Getting Started / overview |
| `/motiva/foundations` | One page, anchored sections: Colors, Gradients, Typography, Spacing, Radius, Shadows, Motion, Accessibility, RTL |
| `/motiva/components/button` | `MButton` documentation + live examples |
| `/motiva/components/card` | `MCard` documentation + live examples |
| `/motiva/components/status-badge` | `MStatusBadge` documentation + live examples |
| `/motiva/components/progress-bar` | `MProgressBar` documentation + live examples |
| `/motiva/components/modal` | `MModal` documentation + live examples |
| `/motiva/playground` | Interactive, parameter-driven playground for Button/Card/Status Badge/Progress Bar |
| `/motiva/changelog` | Summary of Phase 1–4, linking to `CHANGELOG.md` |

## Intended audience

Gradify developers and designers who need to review, test, or approve a
Motiva foundation/component **before** it is adopted by a production screen.
It is not part of the student/lecturer/mentor product experience.

## Development-only status — and its real limitation

The gallery route is wrapped in `MotivaGalleryLayout` (`Client/Shared/
MotivaGalleryLayout.razor`), which checks
`IWebAssemblyHostEnvironment.IsDevelopment()` (injected via the standard
Blazor WebAssembly hosting API — the same mechanism `Server/Program.cs`
already relies on via `app.Environment.IsDevelopment()` /
`UseWebAssemblyDebugging()`). When the environment does not report
`Development`, the layout renders a short "Development only" message
instead of the gallery UI.

**This is a UI-visibility gate, not a security boundary.** Gradify is a
*hosted* Blazor WebAssembly app: the Server compiles and ships the entire
Client bundle (including every gallery page, every gallery component, and
this environment check itself) to any browser that requests it, regardless
of environment. A user who already has the published WebAssembly bundle
(e.g. via a Production deployment mistakenly served without proper
environment configuration, or by inspecting browser dev tools) could still
reach the compiled gallery code. Enforcing genuine Production inaccessibility
would require either:

- excluding the gallery's files from a Release/Production build (an MSBuild
  conditional-compile change), or
- a server-side route block in `Server/Program.cs`/routing.

Both are backend/build changes and are explicitly out of scope for this
phase (see the brief's "Do not change backend logic... or routing outside
the development gallery"). This limitation is intentionally documented here
rather than solved with ad hoc client-side security logic, which would be
insecure and misleading.

The gallery is also not linked from the production navigation
(`AppSideNav`) — there is no other existing "development-only navigation
area" in this codebase to attach to instead.

## File structure

```
Client/Shared/
  MotivaGalleryLayout.razor(.css)        — gallery shell + Development gate

Client/Components/MotivaGallery/
  GallerySidebar.razor(.css)             — left nav + search (Foundations/Components/Playground/Changelog)
  GallerySection.razor(.css)             — titled, anchorable section wrapper
  ComponentExample.razor(.css)           — bordered live-demo stage for a real component
  CodeBlock.razor(.css)                  — <pre><code> Razor snippet renderer + copy button
  TokenSwatch.razor(.css)                — token name + visual swatch + copy button (Foundations only)
  DocPageHeader.razor(.css)              — shared title/subtitle/purpose header (component pages)
  RelatedComponents.razor(.css)          — footer cross-navigation between component pages
  CopyButton.razor(.css)                 — copy-to-clipboard control (reuses gradify.copyToClipboard)

Client/Pages/Motiva/
  MotivaIndexPage.razor(.css)            — /motiva
  MotivaFoundationsPage.razor(.css)      — /motiva/foundations
  MotivaComponentButtonPage.razor(.css)  — /motiva/components/button
  MotivaComponentCardPage.razor(.css)    — /motiva/components/card
  MotivaComponentBadgePage.razor(.css)   — /motiva/components/status-badge
  MotivaComponentProgressPage.razor(.css)— /motiva/components/progress-bar
  MotivaComponentModalPage.razor(.css)   — /motiva/components/modal
  MotivaPlaygroundPage.razor(.css)       — /motiva/playground
  MotivaChangelogPage.razor(.css)        — /motiva/changelog

design/design-system/
  GALLERY.md                             — this file
  CHANGELOG.md                           — Phase 1–4 history
```

Every gallery-only Razor file uses a non-`M`-prefixed name, so there is no
naming collision with the production `M*` design-system primitives
(`MButton`, `MCard`, `MStatusBadge`, `MProgressBar`, `MModal`) they
demonstrate.

## How to add a component example

1. Open the relevant `MotivaComponent*Page.razor` file (or create a new one
   under `Client/Pages/Motiva/` for a future 6th component, and add it to
   `GallerySidebar.razor`'s Components group).
2. Wrap the live instance in `<ComponentExample Title="...">` — always the
   real production component (`MButton`, `MCard`, etc.), never a hand-rolled
   HTML stand-in.
3. Add a `<CodeBlock Code="..." />` below it with the matching Razor
   snippet, written as a plain C# string in the page's `@code` block.
4. If the example needs its own section (e.g. a new state), wrap it in
   `<GallerySection Id="..." Title="...">` so it gets its own sidebar anchor
   (Foundations page) or its own heading (component pages).

## How to add a foundation token example

1. Open `MotivaFoundationsPage.razor`.
2. Add a `<TokenSwatch Name="..." TokenName="--motiva-..." PreviewStyle="..." />`
   inside the relevant `<GallerySection>`. `PreviewStyle` must always
   reference the token via `var(--motiva-...)` — never a hardcoded literal —
   so the swatch is a live reflection of the token, not a second source of
   truth for its value.
3. If it's an entirely new foundation category, add a new `<GallerySection
   Id="...">` and a matching anchor link in `GallerySidebar.razor`'s
   Foundations group.

## How the playground works

`MotivaPlaygroundPage.razor` holds one `@code`-block field per control
(e.g. `_btnVariant`, `_cardPadding`, `_progressValue`), bound to native
`<select>`/`<input>`/`<input type="checkbox">` elements styled specifically
for this page (not new design-system form components). Changing a control
updates Blazor state, which re-renders the real component in the preview
pane and recomputes `GeneratedCode` — a plain C# `switch` expression that
formats the current state into a Razor-shaped string, escaping any
user-typed text for safe display inside `<CodeBlock>`. There is no
general-purpose code-generation engine here — the snippet format is fixed
per component and deterministic from the current control state.

## Responsive behavior

- **Desktop (~1440/1024px):** sticky sidebar (`GallerySidebar`) + main
  content in a grid; foundations/component examples use CSS grid where
  appropriate; the playground is a 2-column `controls | preview` grid.
- **Tablet (~1024px):** sidebar narrows; foundation/component grids drop
  from 4–5 columns to 2; all via CSS media queries, no JavaScript.
- **Mobile (~768px and ~390px):** sidebar becomes a static, wrapped
  top section (not sticky); all grids collapse to a single column;
  playground controls stack above the preview; code blocks scroll
  horizontally inside their own container only (`overflow-x: auto` on
  `.code-block-pre`), never the page itself.

## Known limitations

- The Development-only gate is a UI-visibility check, not a security
  boundary — see above.
- `MModal`'s documented gaps (no focus trap, no body-scroll lock) are
  demonstrated as-is in `/motiva/components/modal`; the gallery does not
  attempt to work around them.
- `MCard`'s interactive-card demo can only distinguish "mouse click" from
  "Enter/Space" by listening for the bubbled `keydown` event on a wrapper
  element (MCard's own `OnClick` callback receives an identical synthetic
  `MouseEventArgs` for both Enter and Space, so the component itself cannot
  report which key was pressed — this is a property of `MCard`, not
  something the gallery works around with new component behavior).
- Patterns and Templates are shown as disabled, "Coming later" sidebar
  entries — no domain/business pattern was implemented in this phase.
