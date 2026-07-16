# Motiva Demo Story — Human Narrative

This is the canonical, human-readable story behind the Development-only demo dataset. Every screen in the product should be evaluated against this same story. For the technical field-by-field specification (exact table rows, statuses, IDs), see `design/demo-data-spec.md` — this document is the narrative it's built from.

**All dates below are relative to the moment the seeder runs ("Day 0" = seed time).** The seeder computes real timestamps by offsetting from `DateTime.Now` at seed time, so the story stays "alive" — genuinely due-soon, genuinely overdue, genuinely recent — no matter when a developer resets and reseeds their database. Nothing is hardcoded to a fixed calendar date.

---

## The team

**Project:** Motiva — Final Project Management Platform (Technological track)

**Students:**
- **נועה כהן (Noa Cohen)** — handles UX research, writing, and specification work.
- **אופיר שרעבי (Ofir Sharabi)** — handles visual/interaction design and wireframing.

**Mentor:** ינאי כרמי (Yanai Karmi) — reviews submissions, gives feedback, recommends on extension requests.

**Course staff:** ג'ני אלון (Jenny Alon) — oversees the course at large; sees every project, not just this one.

---

## The journey so far (Week −9 through Week −1)

- **Week −9 (kickoff):** Team formed, mentor assigned. First meeting with the "client" (the project's fictional stakeholder). Selection & Kickoff stages completed quickly and uneventfully.
- **Week −8 → −7 (definition):** Problem definition finalized — what the platform needs to solve, for whom, and why. Definition stage completed.
- **Week −6 (UX research begins):** The team enters the UX Definition & Design stage (Hebrew: "אפיון"). Noa drafts and completes a research plan.
- **Week −5:** Noa conducts and summarizes user interviews with three fictional stakeholders. Completed.
- **Week −4:** Personas defined from the interview data. User flows mapped for the platform's two core journeys (student flow, mentor flow). Both completed.
- **Week −3:** Ofir completes low-fidelity wireframes for the core screens. Completed. That's 5 of the stage's 8 deliverables done — the stage is now sitting at **62% progress**, which is exactly where "today" finds it.
- **Week −2:** Noa submits the **UX Specification Document** (Google Drive link) for mentor review.
- **~3 days ago:** Yanai reviews it and **returns it with four comments** — the specification is thorough but needs work before the team can move forward:
  1. The primary user persona conflicts with an earlier stated goal — needs reconciling.
  2. The onboarding flow is missing an error-state path.
  3. Accessibility considerations aren't addressed for the mentor-facing screens.
  4. The success metrics section needs to tie back to the original problem definition.
- **~2 days ago:** Realizing the revision plus the upcoming wireframes deadline is tight, Noa opens an **extension request** on behalf of the team, asking for a few extra days on the high-fidelity wireframes deliverable. It's still awaiting Yanai's recommendation.
- **~1 day ago:** A **course-wide announcement** goes out (there's no dedicated announcements feature yet — see the honesty note below — represented as a notification to both students): a reminder that the mid-semester progress review is coming up.
- **~4 days ago → ongoing:** A smaller task, "עדכון סיכום ראיונות משתמשים לפי הערות המנחה" (update the interview summary per the mentor's earlier notes), assigned to Ofir, slipped past its due date and is still open. **This is the one overdue task.**

## Where "today" (Day 0) finds the team

- The **returned UX Specification Document** is the single most urgent thing — it's blocking progress on the next stage and has explicit, actionable feedback waiting.
- The **high-fidelity Wireframes** deliverable is due in **2 days** — not yet submitted, in progress.
- The **Usability Test Plan** (the stage's 8th deliverable) hasn't been started yet.
- One task is **overdue** (Ofir's interview-summary update, ~4 days late).
- One **extension request** is pending Yanai's recommendation.
- One **course announcement**-equivalent notification is unread by both students.
- Ongoing team-wide task: coordinating the weekly status meeting with Yanai (assigned to the whole team, not one person).

This is deliberately not a "everything is on fire" story, nor a "nothing is happening" one — it's an ordinary, believable week for a team that's mostly on track with one real piece of blocking feedback to act on.

## Why this specific shape

- **62% is exact, not approximate** — the Specification stage has 8 linked deliverables, 5 completed. `round(100 × 5 / 8) = 62`. This isn't cosmetic: the app computes stage progress from real milestone-completion counts (`RoadmapStagesController.BuildProgressAsync`), so the dataset has to add up correctly for the UI to show 62% honestly, not because a field was hand-set to "62."
- **"Current stage" is derived, not stored.** A stage becomes "Current" when it's the first stage (in order) with at least one linked milestone that isn't 100% complete. That means every earlier stage (Selection, Kickoff, Definition) needs at least one fully-completed milestone of its own, and every later stage (Development, Evaluation, Submissions & Grading) needs at least one *not-started* milestone so it reads as a genuine upcoming stage rather than "not applicable."
- **One returned submission, one due-soon submission, one overdue task, one pending request, one unread notification** — deliberately one of each of the "attention" categories the product philosophy cares about (§3 of `motiva-product-philosophy.md`: returned > overdue > due-soon > mentor-waiting > else), so every priority tier in the ranking rule has a real example to render against.

## Honesty note: two demo-story items are not fully implementable today

- **"Course announcement"** — there is no `Announcement` entity in the backend (confirmed by direct inspection of `Server/Data/DatabaseMigrator.cs` and every controller — only a client-side TODO comment references the idea). Per the agreed adjustment, this is represented as a `Notifications` row (`Type = "General"`) sent to each team member, clearly marked in `demo-data-spec.md` as a temporary stand-in, not a real feature.
- **"Reply in a project conversation"** — there is no `Conversation`/messaging/thread entity anywhere in the backend (confirmed by exhaustive search). Per your own original conditional ("only if the current data model supports conversations"), this item is dropped from the demo story entirely rather than faked.

## The other four projects (for Course-Staff and lecturer screens)

Lightweight, not part of the primary story, but real enough to exercise Staff-facing filtering and attention states across all three of `ProjectHealthController`'s tiers — Green, Orange, and Red — not just two:

- **"מערכת ניהול ספרייה דיגיטלית"** (Digital Library Management System) — Daniel + Maya, mentored by Yanai. Progressing normally, nothing overdue. Has a real approved submission and a resolved historical request, so it's not just an empty "healthy" placeholder.
- **"פלטפורמת מסחר אלקטרוני לעסקים קטנים"** (E-commerce Platform for Small Businesses) — Idan + Shira, mentored by Yanai. At risk: a 25-day-delayed milestone, a returned submission, and an actively-worked technical-support request.
- **"אפליקציית מעקב בריאות דיגיטלית לקשישים"** (Digital Health Tracking for the Elderly) — Roni + Tal, mentored by **Merav** (a second mentor, so Staff screens show a real multi-mentor roster, not one person carrying every team) — Methodological track, not Technological. Moderately behind (7 days) — the "needs attention" middle tier that was missing before this enrichment pass. Has an extension request sitting at the *lecturer* decision stage, complementing 9001's request which sits at the *mentor* stage — together they demonstrate both steps of that two-stage flow.
- **"אפליקציית אימונים אישית מבוססת AI"** (AI-Based Personal Fitness App) — Yuval + Avigail, mentored by Yanai. Healthy, and deliberately further down the roadmap (currently in the Evaluation stage, not stuck early like the others) — so the roster isn't four projects that all look stuck at the same point.

Each is minimal but real: a couple of completed early stages, one current stage with real tasks and a submission, one request — enough substance to feel alive, not padded for its own sake.
