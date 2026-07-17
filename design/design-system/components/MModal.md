# MModal

**Location:** `Client/Components/Modal/MModal.razor` (+ `.razor.css`)
**Namespace:** `AuthWithAdmin.Client.Components`

## Purpose

Single shared modal shell (overlay + dialog + header/body/footer slots), replacing 9+ independent overlay implementations found by the Phase 1 audit (`tdm-overlay`, `pm-modal-*`, …) across 38 files that touch modal concepts. Not yet wired into any production page — `MentorProfileModal.razor` and other existing modals are untouched in this phase.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `IsOpen` | `bool` | `false` | Nothing renders when `false` |
| `Title` | `string?` | `null` | Rendered as an `<h2>` and auto-linked via `aria-labelledby` |
| `ChildContent` | `RenderFragment?` | `null` | Body content |
| `FooterContent` | `RenderFragment?` | `null` | Optional footer row (e.g. action buttons); omitted entirely when `null` |
| `OnClose` | `EventCallback` | — | Invoked on close-button click, Escape, or backdrop click (if enabled) |
| `CloseOnBackdrop` | `bool` | `true` | |
| `ShowCloseButton` | `bool` | `true` | |
| `Size` | `MModal.ModalSize` | `Medium` | `Small` (`--motiva-container-sm`) / `Medium` (`-md`) / `Large` (`-lg`) max-width |
| `AriaLabelledBy` | `string?` | `null` | Only needed if the caller renders its own heading inside `ChildContent` instead of using `Title` |
| `Class` | `string?` | `null` | |

## Accessibility notes

- Dialog renders `role="dialog"` and `aria-modal="true"`.
- `aria-labelledby` resolves, in order: explicit `AriaLabelledBy` → an auto-generated id tied to the `Title` heading → omitted if neither is set.
- **Escape-key and backdrop-click behavior reuse the existing, already-shipped pattern from `Client/Shared/MentorProfileModal.razor`** — a `tabindex="-1"` overlay with `@onkeydown` checking `e.Key == "Escape"`, auto-focused via `ElementReference.FocusAsync()` on the open transition so Escape works without an extra click first. No new JS interop was introduced, per the Phase 3 constraint.
- Clicking inside the dialog does not close it (`@onclick:stopPropagation="true"` on the dialog element); only a genuine backdrop click (when `CloseOnBackdrop` is true) or Escape closes it.
- The close button has `aria-label="סגירה"` matching the label already used by `MentorProfileModal`'s close button, for consistency.

### Documented accessibility limitation — no focus trap

**Focus is not trapped inside the dialog.** Tab/Shift+Tab can move focus to elements behind the overlay. This matches the existing behavior of every modal already in this codebase (`MentorProfileModal`, `RequestsManagement`'s drawer, `TaskDetailModal`) — none of them implement a focus trap either, and the Phase 3 brief explicitly disallows introducing a new JS dependency solely for focus trapping. A real fix would require either a small JS interop helper (out of scope here) or .NET's newer built-in focus-trap primitives if/when the project's Blazor version exposes one — tracked as a follow-up, not solved in this phase.

## RTL behavior

Overlay/dialog layout uses `display: flex` centering (no `left`/`right`), header/footer use logical `padding`/`gap`, and the close button sits at the flex-end of the header row, which is the reading-start side under `dir="rtl"` automatically — no separate RTL branch needed.

## Usage examples

```razor
<MModal IsOpen="_showModal" Title="פרטי אבן דרך" OnClose="() => _showModal = false">
    <p>תוכן הדיאלוג.</p>
    <FooterContent>
        <MButton Variant="MButton.ButtonVariant.Ghost" OnClick="() => _showModal = false">ביטול</MButton>
        <MButton Variant="MButton.ButtonVariant.Primary" OnClick="Save">שמירה</MButton>
    </FooterContent>
</MModal>

<MModal IsOpen="_confirmOpen" CloseOnBackdrop="false" ShowCloseButton="false" Size="MModal.ModalSize.Small">
    <p>לאשר מחיקה?</p>
</MModal>
```

## Do / Don't

- **Do** pass `FooterContent` for action buttons rather than putting them inside `ChildContent` — the footer gets its own bordered row and consistent spacing.
- **Do** set `CloseOnBackdrop="false"` for destructive-confirmation dialogs where an accidental outside click shouldn't dismiss the choice.
- **Don't** nest an `MModal` inside another `MModal`'s `ChildContent` — z-index tokens (`--motiva-z-overlay` / `--motiva-z-modal`) are shared flat values, not a stack, so nested modals would compete for the same layer.
- **Don't** rely on `MModal` to lock body scroll — see limitation below.

## Known limitations / approved exceptions

- **Backdrop scrim color (`rgba(0, 0, 0, 0.48)`) is a literal, not a `--motiva-*` token.** No token represents a translucent modal scrim in `motiva-tokens.css`; the existing `MentorProfileModal.razor.css` already uses this exact literal for the same purpose, so it's reused here rather than inventing a second, slightly different value.
- **Body scroll is not locked while the modal is open**, per the Phase 3 instruction to preserve existing scrolling behavior unless the project already has a reusable scroll-lock solution — it doesn't (confirmed: no existing modal in the codebase locks `document.body` scroll), so none was added here either.
- No focus trap — see the dedicated section above.
