# Motiva Product Philosophy — Student Workspace

Status: aligned, pending Dashboard design.
Supersedes: any earlier "dashboard = reporting screen" framing.

This document is the shared reference for every future Student-experience screen decision. It is not a visual spec (see `motiva-design-system.md` / `design-tokens.css` / `component-guidelines.md` for that) — it governs *what a screen is for*, before any layout or styling decision is made.

---

## 0. The core shift

We are not designing a dashboard. We are designing **the student's daily workspace** — the place a student naturally starts their working session. Every decision below exists to serve that: helping the student know what to do next, not showing them information about the project.

---

## 1. The workspace, not a report

The student should be able to start their workday here. It orients and directs — it does not merely summarize.

## 2. Four questions, answered through one hierarchy, not four equal zones

Every workspace screen should let a student answer, within seconds:

- What needs my attention?
- What should I do next?
- What's new since my last visit?
- Is my project progressing well?

These are **not four equally-weighted cards.** Answering all four while also spotlighting one action (§4) requires a deliberate hierarchy:

- **Today's Focus** — the single recommended next action, at full visual weight.
- **The Context Strip** — quiet, subordinate signals around it.

If every question gets its own equally-sized card, we've rebuilt the reporting screen this philosophy exists to move away from.

### The Context Strip is content-driven, not fixed slots

The Context Strip does not permanently expose three named destinations (Inbox / Team / Progress). A strip that always shows the same three things becomes wallpaper within days — the exact habituation problem a calm assistant should avoid. Instead it surfaces whichever 2–3 signals are genuinely worth mentioning *right now*, from a legible candidate order:

> Anything requiring awareness that Today's Focus isn't already showing → team health, if not all-clear → an upcoming (non-focus) submission, if soon → project progress, as the reliable fallback.

Two hard rules keep this from becoming its own black box or its own duplication bug:

- **Never repeat Today's Focus.** Whatever item the Focus panel is already surfacing is excluded from the strip's candidate pool.
- **Never pad to fill three slots.** Show what clears the bar — one, two, three, or (rarely) none. Manufacturing a third signal just for symmetry is the §11 failure mode in a new location.

Project progress does not need to appear in the strip every day for §8 to hold — the sidebar's own persistent, compact stage/progress widget already satisfies "remains visible" on every screen, independent of what the Dashboard's strip chooses to show today.

## 3. Priority over feature-completeness — with a legible rule

Information is ordered by urgency, not by what feature produced it. This only works if the ranking is **stated as a rule a student could recite back**, not a black-box score. Working default, open to refinement once we design against real data:

> Returned by mentor (needs revision) → overdue → due within 48h → mentor is waiting on you → unread meaningful Inbox item → everything else.

(Refined from the original draft of this rule: work a mentor has already reviewed and sent back is its own top tier, distinct from and above a plain overdue item — it's not just late, it's actively blocking progress with explicit feedback waiting.)

Every screen that ranks things (Dashboard, the recommendation engine, task lists) should trace back to one shared rule, not invent its own ordering logic independently.

## 4. Motiva recommends; it doesn't just list

Instead of many equally-important cards, the workspace recommends **the single most valuable next action** — with two conditions that make this trustworthy rather than presumptuous:

- **Explainable, not opaque.** The recommendation shows its reasoning ("due tomorrow, blocks your mentor's review"), following the rule in §3. A recommendation a student can't understand is worse than no recommendation.
- **Recommending ≠ hiding.** Everything else that also needs attention stays visible in the Context Strip or the My Tasks list (§2, §6) — Today's Focus surfaces the *most* valuable action, it doesn't suppress the others.

## 5. Notifications and Inbox are two jobs on one event stream, not two products

They coexist, but only if their responsibilities stay distinct — same underlying events, two different jobs (the Gmail model: a push alert and the inbox list are two views of the same message, not two separately-curated pipelines):

- **Notifications** (header bell): ambient, transient, low-threshold. Fires on nearly anything, disappears once seen, often deep-links elsewhere (into an Inbox item, or directly to a task/submission).
- **Inbox**: durable, curated, actionable. Only the meaningful subset lands here — mentor comments, returned submissions, course announcements, conversation replies, important project updates — with read/archive state that persists.

Open item to verify before implementation: "course announcements" and "conversation replies" need to exist as real entities before Inbox can show them — confirm against the current data model rather than assuming the UI can be designed first and the backend caught up later.

## 6. Tasks are the heart of the workspace — but the Dashboard and the Tasks page ask different questions

- **Dashboard → "What should I do?"** Shows only tasks that require *my* action — assigned to me individually, or to the team as a whole (no specific individual owner). This is a personal actionable queue, not a project board. Each row exposes due date, stage, and status; "assignee" isn't useful here since every visible row is implicitly mine to act on.
- **Team Progress (Dashboard)** — a lightweight, separate summary: are teammates on track, blocked, or overdue? A glance, not a list. This satisfies "awareness of the team" without turning the workspace into a management board.
- **Tasks page → "How is the team progressing?"** The full, inspectable team task list (with assignee, since rows now belong to different people) lives here, not on the Dashboard. Flagged, not designed yet: this is a real scope change from the page's current personal-only view — worth its own critique/proposal pass when we get there, not assumed now.

## 7. Submissions communicate state, not files

Returned for revision / waiting for review / upcoming — the status *is* the content. Already the direction established in the Tasks and Submissions work done earlier; this principle just makes it explicit and universal.

## 8. Progress stays visible, never dominant

A compact signal (slim bar or small ring — never both in the same surface), present wherever it's contextually relevant, never the largest thing on the screen.

## 9. Quick Actions: deferred, and started simply

The radial macOS/Arc-style menu is deferred — it's a low-discoverability, accessibility-risky pattern better suited to high-frequency power-user tools than an occasional-use academic product, and it directly risks reading as novelty rather than calm (§10). Quick Actions instead start as a plain, proven pattern: a simple button or a Cmd/Ctrl-K style command palette. If we build the underlying actions as a registry (id → label → icon → handler) rather than hardcoded per-page buttons, the door stays open to a richer presentation later without a rewrite.

## 10. Calm assistant, not administration software

Reference points: Linear, Notion, Arc, Apple. Explicitly not: ERP systems, BI dashboards, admin panels. Every principle above should be read through this filter — including §2's hierarchy and §4's recommendation, which could easily curdle into something that feels bossy or alarmist if not handled with restraint.

## 11. "All caught up" is a first-class state, not an edge case

Every prioritized/recommending surface needs a deliberately designed calm state for when nothing is overdue, the Inbox is empty, and progress is healthy. This is likely the *most common* state a student will see, and it's the truest test of §10: does the workspace stay quiet and reassuring when there's nothing urgent to say, or does it manufacture importance to fill the space? The latter is a real failure mode worth explicitly guarding against, not an unlikely corner case.

---

## Open items carried forward (not blocking, but not forgotten)

- Tasks page scope change (§6) — needs its own critique/proposal pass when we get to that screen.
- Inbox content types (§5) — verify "announcements" and "conversation replies" exist or need to be scoped as new backend work before Inbox is designed against them.
- Priority rule (§3) — the stated default is a starting point for the Dashboard design conversation, not a final ranking; expect to refine it against real task/submission data.
