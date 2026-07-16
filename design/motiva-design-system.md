# Motiva Design System

**Status:** Visual direction approved (`design/motiva-concept-board.html`). This document extracts the reusable design language from it. Implementation reference, not a redesign spec — apply incrementally, per screen, per epic.

**Source of truth order:** the concept board is the visual inspiration → this document + `design-tokens.css` are the implementation reference → `component-guidelines.md` is the per-component how-to. If the concept board and this document ever disagree, this document wins for implementation (it's the one meant to be built from); flag the conflict rather than silently picking one.

---

## 1. Product personality

Motiva behaves like a capable teaching assistant who already knows exactly where you are in the project — not a filing system waiting to be searched. Every screen should let a student answer, immediately:

- **Where am I?**
- **What matters most right now?**
- **Where am I going next?**

Six qualities, translated into behavior (not vocabulary):

| Quality | What it means in the interface |
|---|---|
| Calm | No more than one loudly-colored/urgent element visible at a time |
| Guiding | Every screen states its single most important action as a sentence near the top |
| Professional | Encouraging copy is always paired with a precise fact (a date, a count) |
| Modern | State changes animate (150–250ms), never an instant snap |
| Friendly | Copy is written directly to the student, second person, active voice |
| Progress-oriented | Some sense of forward motion is visible on every primary page, not only Dashboard/Stages |

**The One Focus Rule:** exactly one element carries full visual weight per screen; everything else is deliberately quieter, never equal. This wasn't planned as a system — it's the same instinct behind the Tasks page's hero-vs-quiet split and the Stages page's current-vs-compact split, now made an explicit standard every new screen gets checked against.

---

## 2. Color

Sampled directly from the real logo's gradient (violet-purple → blue → turquoise) — not a generic palette with brand colors bolted on. Full values and CSS variables live in `design-tokens.css`; this is the semantic map:

| Role | Token | Use for |
|---|---|---|
| Primary action | `--g-accent` (indigo) | Buttons, links, focus states |
| Signature | `--g-brand-purple` / `--g-brand-gradient` | The ONE current-stage / hero moment per screen — never decoration |
| Info | `--g-info` (blue) | Waiting-on-someone-else states |
| Success | `--g-success` (turquoise) | Completed, approved, done |
| Attention | `--g-attention` (amber) | Needs the student's action, overdue, due soon |
| Danger | `--g-danger` (red) | Destructive actions, real errors only |

**Danger is deliberately outside the brand family** — red stays a universal safety convention, not a brand color. Everything else should draw from the gradient family instead of a new one-off hex.

**Neutrals carry a whisper of indigo**, not slate-grey unrelated to the brand — `--g-bg-page`, `--g-bg-surface`, `--g-text-primary`, etc. Don't reach for pure `#fff`/`#000`/generic grey when a token exists.

**Never re-invent a status color.** Before writing a new hex for "done" or "overdue" in a component's own CSS, check `--g-success` / `--g-attention` / `--g-info` first — this exact duplication (six components, six slightly different greens) is what the token system exists to stop.

---

## 3. Typography

One family — Assistant — carried by weight and size, not by mixing typefaces. Hebrew has no ascenders/descenders, so the same pixel size that feels right for English body text reads smaller and denser in Hebrew; the whole scale sits higher than a typical English SaaS scale, and line-height stays looser.

| Token | Size | Use |
|---|---|---|
| `--g-type-display` | 48px | Hero headlines only |
| `--g-type-page-title` | 32px | Page-level heading, greeting |
| `--g-type-section` | 25px | Section titles |
| `--g-type-card` / `--g-type-subtitle` | 19px | Card titles, sub-headings |
| `--g-type-body` | 18px | **Base size** — paragraphs, nav items, task titles, buttons |
| `--g-type-secondary` | 15px | Captions, meta text, dates |
| `--g-type-micro` | 13px | Badges, tiny labels, eyebrows |

Body line-height: 1.65 (1.7 for longer reading paragraphs). Headings: 1.25. Use `font-variant-numeric: tabular-nums` for anything counted or dated so columns of numbers align.

---

## 4. Spacing, radius, elevation

**Spacing** (`--g-space-xs/sm/md/lg/xl` = 4/8/16/24/32px) is unchanged — it was already reasonable. Bigger type needs more breathing room around it: when a component's text grows, its padding should grow with it (see `component-guidelines.md`).

**Radius** — rounded geometry echoing the logo's own soft terminals. Vary the scale deliberately, don't apply one radius everywhere:

| Token | Size | Use |
|---|---|---|
| `--g-radius-sm` | 10px | Buttons, small controls, nav items |
| `--g-radius-md` | 14px | Standard cards |
| `--g-radius-lg` | 20px | Larger content cards, page-level panels |
| `--g-radius-xl` | 28px | Signature/hero cards only |
| `--g-radius-full` | pill | Badges, avatars |

**Elevation** — five tiers, tinted with brand indigo instead of neutral grey. The signature tier (`--g-shadow-glow`) is reserved for exactly one element per screen; using it twice on the same screen defeats the point.

| Tier | Token | Use |
|---|---|---|
| Flat | border only | Reference lists, secondary content |
| Resting | `--g-shadow-sm` | Standard cards, rows |
| Raised | `--g-shadow-md` | Hover states, popovers |
| Lifted | `--g-shadow-lg` | Modals, floating panels |
| Signature | `--g-shadow-glow` | The one focus per screen |

---

## 5. Page hierarchy (layout rhythm)

Every primary page follows the same three-zone structure — not new, but now the standing rule rather than an accident that happened twice:

1. **Hero / Context** — who, where, what stage. Gradient-wash background, quiet.
2. **Primary content** — the one thing that matters now. Elevated surface, full visual weight.
3. **Secondary / reference** — everything else. Flat, quiet, visible but never loud.

See `component-guidelines.md § Page hierarchy` for the concrete per-page checklist.

---

## 6. Motion

Purposeful and fast, never decorative or bouncy:

- State changes (expand, status change, progress fill): 150–250ms ease.
- The signature/current-stage indicator may use a slow ambient animation (e.g. a rotating gradient ring, ~5–6s) to read as "alive" without demanding attention.
- Always respect `prefers-reduced-motion: reduce` — disable ambient/decorative animation, keep functional transitions instant.

---

## 7. Accessibility

- Color is never the only signal — pair every status color with an icon or text label (a badge says "הושלם", not just green).
- Maintain WCAG AA contrast for text on its background; the muted/secondary tokens are tuned to pass on both `--g-bg-surface` and `--g-bg-page`.
- Every interactive element gets a visible focus state (outline or ring using `--g-accent`).
- Respect `prefers-reduced-motion`.
- RTL is not an afterthought: `direction: rtl` must be set explicitly on flex/grid containers that need it — inheriting from `<html dir="rtl">` alone is not reliable for flex ordering (confirmed the hard way while building the concept board).

---

## 8. What this system does not cover yet

Deliberately out of scope for this pass: data tables, form validation states beyond basic inputs, charts/graphs, complex multi-step wizards. Extend the token set when one of these is actually needed for a real screen — don't pre-design for hypothetical future components.
