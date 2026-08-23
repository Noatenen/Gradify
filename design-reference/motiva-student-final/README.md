# Motiva — Student experience, FINAL (source of truth)

**Canonical source:** Claude Design project `Motiva visual language exploration`
`https://claude.ai/design/p/69ed4d85-4c23-4abc-83a9-719448d972eb`

Authoritative files (read 2026-08-22 via the design MCP):

| File | Screen |
|---|---|
| `Motiva Student dashboard final.dc.html` | בית / dashboard |
| `Motiva Student Tasks final.dc.html` | המשימות שלי |
| `Motiva Student Requests final.dc.html` | בקשות |
| `Motiva Student Calendar final.dc.html` | יומן ותכנון |
| `Motiva Student Resources final.dc.html` | משאבים |
| `Motiva Student Project final.dc.html` | הפרויקט שלי |
| `Motiva Student Profile final.dc.html` | הפרופיל שלי |
| `Motiva Notifications Popover final.dc.html` | notifications popover (global chrome) |

`support.js` in that project is the **dc-runtime** — the canvas renderer that
interprets `<x-dc>`, `sc-for`, `sc-if` and `{{ }}` bindings. It is prototype
infrastructure, not product code and not a design spec: nothing in it should be
ported. `motiva-logo.png` is the real wordmark, already shipped at
`Client/wwwroot/images/motiva-logo.png`.

Everything under `design-reference/motiva/`, `design-reference/project-details/`
and `design-reference/mentor-experience/` predates these files. **Where they
disagree, these files win.** The older boards describe a sidebar shell, a
`#4F46E5` violet and a "three semantic colours only" rule — all three are
superseded below.

---

## 1. Shell — a top bar, not a sidebar

Every one of the eight screens draws the same chrome:

```
[ logo 26px ]        ( בית · משימות · בקשות② · יומן · משאבים · הפרויקט שלי )        [ ○ ○ ○ ◉ ]
```

* **Logo** — `motiva-logo.png`, `height:26px`, at the reading start.
* **Nav** — one centred pill group. Track `rgba(27,26,34,.045)`, `border-radius:999px`,
  `padding:4px`, item gap `2px`.
  * active item: `background:#FFFFFF`, `font:700 13.5px/1 Heebo`, `padding:9px 18px`,
    `box-shadow:0 1px 2px rgba(27,26,34,.08)`
  * idle item: `color:#6B6779`, `font:500 13.5px/1 Heebo`, `padding:9px 16px`, hover `color:#1B1A22`
  * בקשות carries a count pill: `background:#6D28F5`, `color:#fff`, `font:600 10px/16px`, `min-width:16px`
* **Right cluster** — 38px circles, `background:rgba(255,255,255,.7)`, hover `#fff`,
  icon `16px` stroked SVG, `color:#4A4657`: search · calendar · bell · avatar.
  * bell badge: `background:#E0335F`, `border:2px solid #F7F7FB`, `min-width:17px`, top/left `-2px`
  * avatar: `linear-gradient(140deg,#6D28F5,#2E63E8 55%,#0FA8C2)`, white initials `600 13px`
* **Calendar circle is optional** — present on dashboard/project, absent on the
  calendar screen itself and on profile/resources. It is a shortcut, not a fixture.

There is **no sidebar** on any student screen.

## 2. Page frame

```
body            #E9EAEF + three radial washes, padding 26px, content centred
canvas          max-width 1460px, border-radius 36px, padding 20px 28px 28px
                background: 3 radials over linear-gradient(180deg,#F7F7FB,#F3F4F9)
                box-shadow: 0 2px 3px rgba(27,26,34,.04), 0 40px 70px -50px rgba(27,26,34,.4)
```

Body wash (verbatim):
```css
radial-gradient(55% 45% at 12% -5%, rgba(109,40,245,.10), transparent 62%),
radial-gradient(60% 50% at 55% 50%, rgba(46,99,232,.06), transparent 65%),
radial-gradient(50% 45% at 95% 105%, rgba(15,168,194,.10), transparent 62%), #E9EAEF
```
Canvas wash (verbatim):
```css
radial-gradient(70% 60% at 88% -10%, rgba(109,40,245,.09), transparent 60%),
radial-gradient(60% 55% at 4% 30%, rgba(62,107,224,.06), transparent 58%),
radial-gradient(55% 50% at 60% 115%, rgba(15,168,194,.08), transparent 60%),
linear-gradient(180deg,#F7F7FB,#F3F4F9)
```

Page title block: `padding:14px 4px 20px`, `h1 { font:700 38px/1.1 Heebo; letter-spacing:-.035em }`,
optional subtitle `font:500 15px/1 Heebo; color:#6B6779; margin-top:10px`.
Controls sit on the same row, pushed with `margin-inline-start:auto`.

## 3. Colour — four roles, not three

| Role | Fill | Ink | Tint |
|---|---|---|---|
| Brand / action / current stage | `#6D28F5` | `#6D28F5` | `rgba(109,40,245,.08–.10)` |
| In progress / assignee / task | `#2E63E8` | `#2E63E8` | `rgba(46,99,232,.09–.11)` |
| Done / synced / team task | `#0FA8C2` | `#0F8FA6` | `rgba(15,168,194,.10–.12)` |
| Attention / overdue / destructive | `#E0335F` | `#C42B52` | `rgba(224,51,95,.06–.10)` |

Journey gradient stops (stage → stage): `#0FA8C2 → #1E86D5 → #2E63E8 → #4C47EE → #6D28F5`.
Connected-service "live" dot: `#19B27B`. Neutral/inactive: `rgba(27,26,34,.045–.07)` on `#6B6779`.

Ink ramp: `#1B1A22` · `#4A4657` · `#6B6779` · `#9B96A8` · `#B4B0BF` / `#C5C2D0`.
Lines: `rgba(27,26,34,.06)` (row) · `.07` (section) · `.12` (control edge).

Signature gradient: `linear-gradient(to left,#6D28F5,#2E63E8 60%,#0FA8C2)`.
Avatar gradient: `linear-gradient(140deg,#6D28F5,#2E63E8 55%,#0FA8C2)`.
Toggle-on gradient: `linear-gradient(to left,#6D28F5,#2E63E8)`.

## 4. Surfaces and radii

| Surface | Value |
|---|---|
| Section card | `rgba(255,255,255,.66)` · `1px solid rgba(255,255,255,.85)` · r24 · `0 1px 2px rgba(27,26,34,.04), 0 14px 30px -26px rgba(27,26,34,.35)` |
| Solid card | `#FDFDFE` · same border/shadow · r24 |
| Muted section (טופלו) | `rgba(255,255,255,.5)` · `1px solid rgba(255,255,255,.7)` · `0 1px 2px rgba(27,26,34,.03)` |
| Inner card | `#fff` · `1px solid rgba(27,26,34,.06–.07)` · r16–20 |
| Modal | `#fff` · r26 · `0 2px 4px rgba(27,26,34,.06), 0 30px 60px -30px rgba(27,26,34,.5)` · w440–460 |
| Side panel | fixed, `top/bottom:26px`, `inset-inline-end:26px`, w360, r26, same shadow |
| Popover | `#FDFDFE` · `1px solid rgba(27,26,34,.07)` · r22 · w392 · `0 2px 4px rgba(27,26,34,.05), 0 28px 55px -28px rgba(27,26,34,.45)` |
| Backdrop | `rgba(27,26,34,.16)` |

Radius scale: 36 shell · 26 modal/panel · 24 section · 22 popover · 20 expanded row ·
18 card/form · 16 inner · 14 notif row · 12 input · 11/10 icon chip · 7 event chip · 999 pill.

## 5. Controls

* **Primary CTA** — gradient pill. `padding:11–14px 18–26px`, `border-radius:999px`,
  `color:#fff`, `font:600 13–15px/1 Heebo`, hover `filter:brightness(1.08)`. No shadow.
* **Disabled CTA** — `background:rgba(27,26,34,.06)`, `color:#9B96A8`, `cursor:default`.
* **Secondary** — `border:1.5px solid rgba(27,26,34,.12)`, pill, `color:#4A4657`,
  hover `border-color:rgba(27,26,34,.3); color:#1B1A22`.
* **Gradient-border ghost** — `background:linear-gradient(#fff,#fff) padding-box,
  linear-gradient(to left,#6D28F5,#2E63E8 60%,#0FA8C2) border-box`, `border:1px solid transparent`.
* **Segmented tabs / filter chips** — same pill-group recipe as the nav.
* **Input** — r12, `1px solid rgba(27,26,34,.12)`, `background:#FBFBFD`, `font:500 13.5px/1.2`.
  Focus ring globally: `outline:2px solid rgba(109,40,245,.35); outline-offset:0`.
* **Toggle** — 42×24 track r999; on = toggle gradient, knob flush start; off =
  `rgba(27,26,34,.14)`, knob flush end. Knob 18px white, `0 1px 2px rgba(27,26,34,.25)`.
* **Icon button** — 30px circle, `color:#9B96A8`, hover `background:rgba(27,26,34,.05); color:#1B1A22`.
* **Checkbox (task row)** — 18px, r6, `1.7px solid #CDCAD8`; checked `#0FA8C2` fill + white tick.

Iconography is **stroked SVG** (`stroke-width:1.8–2.2`, round caps/joins), 12–16px.
No icon-font glyphs anywhere in the final design.

## 6. Per-screen structure

**Dashboard** — h1 greeting → `מסע הפרויקט` journey stepper → grid `1fr / 1.4fr`
(`לשים לב` focus card + `בקשות`) → flex row (`הבאות בתור` tasks 1.6/400px + `התראות` 1/280px
+ `מועדים קרובים` 1/260px + `השבוע ביומן` 1/260px). Task-detail and create-team-task modals.

**Journey stepper** — one flex row; completed stages `flex:1`, current `flex:1.7`, last `flex:0 0 auto`.
Completed: 18px filled dot + white tick, connector `linear-gradient` between the two stage colours.
Current: 18px white dot with `4px solid #6D28F5` ring, connector `#DCD8EA` with a 62% gradient fill,
label `700 14px #6D28F5` + `— 62%`. Future: 16px white dot `2px solid #CDCAD8`, connector `#E2E0EB`.

**Tasks** — h1 + current-milestone line + controls → `ATTENTION` band → `משימות הפרויקט`
→ `משימות צוות` (inline create/edit with optional work-time scheduling) → new-version upload modal.

**Requests** — h1 + status tabs + type dropdown + `+ בקשה חדשה` → one section per group
(`דורש ממך פעולה` r24 tinted label `#C42B52`, `בטיפול`, `טופלו` muted). Rows collapse to a
dot+title+meta strip and expand into an r20 white card with the request text, a date-change
block, the response quote (`rgba(46,99,232,.06)`), an inline reply row and a history accordion.

**Calendar** — h1 + month/week segmented + period stepper + `+ אירוע / משימה` → inline
gradient-bordered create form → month grid (7 cols, `min-height:104px` cells, today tinted
`rgba(109,40,245,.035)` with a violet pill date) or week grid (52px gutter, 56px/hour rows,
`#E0335F` now-line) → type legend → 360px detail panel.
Event types: משימה `#2E63E8` · משימת צוות `#0FA8C2` · מועד פרויקט `#4A4657` · פגישה `#6D28F5` · הגשה `#C42B52`.

**Resources** — h1 + search pill → `רלוונטי לשלב שלך` gradient-bordered strip → library
section with filter chips (`הכל/תבניות/מדריכים/דוגמאות/כלים/הקלטות/מועדפים`), a
`repeat(auto-fill,minmax(300px,1fr))` card grid with per-card type icon, star favourite and
action link → 360px detail panel.

**Project** — h1 + gradient-text project name + tagline + team chip → journey stepper
(clickable, opens an inline stage strip) → grid `1.55fr / 1fr`: project details (read/edit)
+ `הצוות` and `ליווי והנחיה` → `קישורי הפרויקט` pill row with inline add/edit form.

**Profile** — 64px gradient avatar + name `700 32px` + role line → grid `1.6fr / 1fr`:
`פרטים אישיים` (solid `#FDFDFE`, 2-col field grid, edit/save) + `העדפות` (toggle rows)
| `הצוות שלי` + `שירותים מחוברים` (Google Calendar connect/disconnect).

**Notifications popover** — anchored under the bell, `top:46px; left:0`, w392, r22.
Header `התראות` + unread count pill + `סימון הכל כנקרא`; rows are r14, 32px r10 icon chip,
6px unread dot (`#6D28F5`, or `#E0335F` when urgent), read rows at `opacity:.62`;
footer link toggles `כל ההתראות` / recent only. Empty state: teal check circle.
