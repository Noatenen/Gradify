# Phase 2 — Vertical Slice Plan

Restructures Phase 2 of `design/business-logic-consolidation-epic.md` (previously a server-first, layer-by-layer migration — see `design/migration-dependency-map.md`) into six vertical slices. Each slice ships one full experience — service → controller → screen — so a visible product improvement lands at the end of *every* slice, not only at the end of the epic. **No code is changed by this document.**

## The gate every slice shares

No slice starts until the previous one clears all three:
1. **Tests pass** — the slice's own focused regression tests (below), plus the existing 47 Phase 1 unit tests, plus the full existing test suite.
2. **Live demo data checked** — the slice's before/after comparison run against the real seeded demo dataset (not synthetic fixtures), for at least the specific projects/users already known from the walkthrough/consistency audits to have shown disagreement.
3. **Affected screen reviewed** — a human (you) opens the actual screen as the actual role, side-by-side with the pre-migration behavior, and signs off.

Each slice below states what "reviewed" concretely means for that slice's screen, since "review it" is meaningless without knowing what to look at.

---

## Slice 1 — Project Stages

**Scope**
- Service: `ProjectRoadmapService`
- Server: `RoadmapStagesController` — its private `BuildProgressAsync` body is replaced by a call to `ProjectRoadmapService.ComputeRoadmapProgress`. Because all three of its public endpoints (`/my-progress`, `/projects/{id}/progress`, `/mentor/projects-progress`) already share this one method, the server-side swap lands for all three simultaneously — that's unavoidable, it's the same code today.
- Client (this slice only): `Client/Pages/Stages/StudentStagesPage.razor` (`/project-stages`) — updated to explicitly label `CurrentStageProgressPct` ("התקדמות בשלב הנוכחי") and `OverallProjectProgressPct` ("התקדמות כוללת בפרויקט") as two distinct numbers, per your standing requirement that they never be presented as interchangeable.
- **Deliberately deferred**, even though the server response already carries the new fields after this slice: `MentorRoadmapOverviewPage.razor`'s consumption of them — that's Slice 4, so a mentor-facing UI change doesn't sneak in under a "student" slice.

**Visible change**: **Minor, additive.** `RoadmapStagesController`'s three endpoints already had zero divergence before this slice (confirmed in the audit), so the underlying per-stage numbers don't change. The only user-visible delta is the Student Project Stages page gaining two clearly-labeled top-line numbers it may not have surfaced this explicitly before. Low risk.

**Before/after verification**: capture the full JSON response of all three endpoints for every project in the demo dataset before the swap; diff against the same call after the swap. Every field that existed before (`Stages[].ProgressPct`, `CurrentStageCode`, `ScheduleStatus`, `Upcoming`, `Overdue`) must be byte-identical. Only the two new fields are allowed to appear.

**Focused regression tests**
- Golden-master test: snapshot all three endpoints' output for the full demo dataset pre-migration; assert identical post-migration (new fields aside).
- Reuse the 8 existing `ProjectRoadmapServiceTests` (already passing) as the unit-level guarantee for the pure function itself — no new pure-logic tests needed here, only the controller-wiring characterization test above.

**Review checkpoint**: log in as a student with a mid-semester project, open `/project-stages`, confirm the stage timeline looks exactly as before, and confirm the two new percentages are visibly distinct and separately labeled (not "62%" and "52%" sitting next to each other unexplained).

---

## Slice 2 — My Tasks

**Scope**
- Service: `TaskUrgencyService`
- Server: `ProjectsController.GetMyTasks` — its inline `IsUrgent` SQL is replaced by a call to `TaskUrgencyService.GetTaskUrgencyForProjectAsync`, and the response now also carries `AttentionReason`/`AttentionRank` per task.
- Client: `Client/Pages/Tasks/StudentTasksPage.razor` + `Client/Pages/Tasks/TaskStatusHelpers.cs` — the three tiers (Action Required / Waiting on Others / Upcoming) are regrouped from `AttentionReason` instead of `TaskStatusHelpers.TaskRequiresAction`/`ResolveDisplayStatus`. `GetDateDisplay`'s day-count *formatting* is kept; its independent overdue *decision* is deleted in favor of reading `IsOverdue`.

**Visible change**: **Yes.** `GetMyTasks`'s `IsUrgent` flag was already the canonical formula (computed today, just never read) — so tasks it flags will not change. But `TaskStatusHelpers`'s own logic (browser-local dates, no per-team override awareness) will disagree with it for some tasks, meaning **some tasks will visibly move between tiers**, and the UTC-vs-local gap closes (a task showing "overdue" on one browser timezone and not another today will now be consistent). This is a real behavior change, not a no-op, even though the underlying flag itself is unchanged.

**Before/after verification**: for every student/project in the demo dataset, list each task's tier under the *old* `TaskStatusHelpers` logic and the *new* `AttentionReason`-based logic side by side. Every task that changes tier must be individually explainable (e.g., "task X was in Upcoming under local-time due-date check, is now Action Required because it's actually overdue in UTC") — not just asserted equal or silently accepted.

**Focused regression tests**
- Unit tests already exist for `TaskUrgencyService.ComputeUrgency` (Phase 1, 21 tests) — no new pure-logic tests needed.
- New: a tier-mapping characterization test — for the full demo dataset, assert the *set of tasks per tier* matches a recorded expected diff list (not "unchanged"), so any future accidental tier shift is caught even if this migration's own diff was intentional.
- New: delete-safety test — grep-based check (or a compile-time assertion) that `TaskStatusHelpers.TaskRequiresAction` and the old `ResolveDisplayStatus` are no longer referenced by `StudentTasksPage` after the swap (they're not deleted yet — that's Slice 6 — but nothing should call them anymore).

**Review checkpoint**: log in as a student with at least one task affected by a UTC-vs-local boundary (per the before/after diff), open `/tasks`, confirm the three tiers match the expected diff exactly, and confirm no task silently disappeared from all three tiers.

---

## Slice 3 — Student Dashboard

**Scope**
- Services: `TaskUrgencyService` (for `ActionCenterCard`, `UpcomingSubmissionsCard`) + `ProjectRoadmapService` (for `StudentDashboardHero`)
- Server: none new — `ProjectsController.GetMyDashboard` starts returning the already-existing `TaskUrgencyDto`/roadmap fields alongside its current payload (additive DTO change only).
- Client:
  - `Client/Pages/Dashboard/ActionCenterCard.razor` — inline overdue + priority logic deleted, replaced by reading `AttentionReason`/`AttentionRank` directly, sorted/capped at 3 in markup only.
  - `Client/Pages/Dashboard/UpcomingSubmissionsCard.razor` (or current equivalent under `Client/Pages/Dashboard/`) — its two browser-local overdue formulas deleted, replaced by reading `IsOverdue`.
  - `Client/Pages/Dashboard/StudentDashboardHero.razor` — `_overallPct` (integer-truncated) deleted, replaced by `OverallProjectProgressPct`/`CurrentStageProgressPct`, explicitly labeled per Slice 1's convention.

**Visible change**: **Yes — the largest single-screen change in the epic.** This is the exact screen where the original "62% vs 52%" discrepancy was first spotted, and where `ActionCenterCard`'s capped top-3 list can change *membership*, not just values, because the canonical `AttentionRank` recognizes two reasons (`PendingMentorReview`, `DueSoon`) the card doesn't check for today.

**Before/after verification**:
- `StudentDashboardHero`: capture the currently-displayed percentage for every demo-dataset project; compare against `OverallProjectProgressPct`/`CurrentStageProgressPct`; every change must be traceable to truncation-vs-rounding or flat-vs-stage-weighted averaging, not unexplained drift.
- `ActionCenterCard`: capture the current top-3 list per student; compare against the new ranked list; every membership change must be traceable to a `PendingMentorReview`/`DueSoon` task that previously had no reason to surface.
- `UpcomingSubmissionsCard`: capture the current overdue flag per task; compare against `IsOverdue`; every flip must be traceable to the UTC-vs-local gap.

**Focused regression tests**
- Underlying pure-function tests already exist (Phase 1). New: three characterization tests (one per component) against the full demo dataset, each asserting the recorded expected-diff list rather than blind equality — mirroring Slice 2's approach.
- New: a single "same task, same verdict" cross-check test — for every task shown on both `/dashboard` (`ActionCenterCard`) and `/tasks` (Slice 2's tiers), assert `AttentionReason` is identical on both screens. This is the direct regression test for the original inconsistency this whole epic exists to fix.

**Review checkpoint**: log in as two or three different students (varied project states — one with an overdue task, one with a returned submission, one fully on-track), open `/dashboard`, confirm the hero percentage, action-center list, and upcoming-submissions list all look correct and none contradict `/tasks` for the same tasks.

---

## Slice 4 — Mentor views

**Scope**
- Services: `ProjectHealthService` + `ProjectRoadmapService` + `TaskUrgencyService` (all three converge here — `MentorController` is the one place today that hand-rolls its own version of all three concepts in a single file)
- Server: `MentorController` — both endpoints (`GetProjects`, `GetProjectDetail`) swapped to call the three services instead of: reading raw `Projects.HealthStatus`; computing flat `MilestoneProgressPct`/`TaskProgressPct`; and running its own three overdue formulas (including the confirmed self-contradiction between the aggregate count and the per-task `IsOverdue` on the same page).
- Client:
  - `Client/Pages/Mentor/MentorProjectsPage.razor` (`/mentor/projects`)
  - `Client/Pages/Mentor/MentorProjectDetailPage.razor` (`/mentor/projects/{id}`)
  - `Client/Pages/Mentor/MentorRoadmapOverviewPage.razor` (`/mentor/roadmap`) — this is where Slice 1's deferred client change lands: `CalcOverallPct` is deleted, the page reads `OverallProjectProgressPct` directly.

**Visible change**: **Yes.** A real Health badge (Green/Orange/Red) appears on `/mentor/projects` and `/mentor/projects/{id}` for the first time in practice (the raw column had no writer). The progress percentage may shift (flat ratio → stage-weighted average). Most importantly, `GetProjectDetail`'s **confirmed internal self-contradiction is fixed** — the page's aggregate overdue count and each task's own `IsOverdue` badge will finally agree.

**Before/after verification**: for every project a mentor can see in the demo dataset, snapshot today's Health badge (or blank, if the column was never written), progress %, and both overdue numbers (aggregate + per-task); compare against the new values. The self-contradiction fix should be called out explicitly per project where it applied — "previously showed N overdue in the summary but M as red badges below; now both show M."

**Focused regression tests**
- New: a pinned regression test for the specific demo project where `GetProjectDetail`'s two overdue counts disagreed — asserts they are now equal, with the old (wrong) numbers documented in a comment so the fix stays traceable.
- New: `MentorController` response-shape test confirming `Health`/`Progress`/`Urgency` fields match direct calls to the three services for the same project (no re-derivation happening in the controller).
- Reuse: `MentorRoadmapOverviewPage`'s number should exactly match Slice 1's already-verified `OverallProjectProgressPct` — one spot-check, not new logic.

**Review checkpoint**: log in as a mentor with 3+ projects of varied health, open `/mentor/projects`, then drill into the one with previously-contradicting overdue counts, confirm the summary and per-task badges now agree, then open `/mentor/roadmap` and confirm its percentage matches what Slice 1 already showed on the student side for the same project.

---

## Slice 5 — Lecturer and Staff views

**Scope**
- Service: `ProjectHealthService` (primary) + `ProjectRoadmapService` (current-stage, for `MilestonesOverviewController`)
- Server, in ascending risk order within this slice:
  1. `ManagementController` (`/management/projects`) — raw-column read replaced.
  2. `ProjectOverviewController` (`/projects/{id}/overview`) — raw-column read replaced; its own task-completion ratio is *relabeled* ("השלמת משימות"), not replaced.
  3. `MilestonesOverviewController` (`/milestones-overview`) — its own three-value `Overdue`/`NeedsAttention`/`Healthy` per-row status replaced by `ProjectHealthService` + `ProjectRoadmapService`'s current-stage. This requires an explicit label-mapping decision (old taxonomy → Green/Orange/Red) before merging — flag for your sign-off separately from the code review.
  4. `LecturerDashboardController` (`/dashboard/mentor`, `/dashboard/lecturer`) — **last, and the highest-risk single change in the entire epic.** Its composite score (`100 − overdue×10 − missing×8 − oldOpenReqs×5 − ...`) is replaced by `ProjectHealthService`'s `Status`/`DelayDays`; `OpenRequestCount`/`OldOpenRequestCount`/`MissingMandatorySubmissionCount` become separate labeled fields, never blended back into one score.
- Client:
  - `Client/Pages/Management/Projects/ProjectsManagement.razor`
  - `Client/Pages/Projects/Overview/ProjectOverviewPage.razor`
  - `Client/Pages/Milestones/Overview/MilestonesOverviewPage.razor`
  - `Client/Pages/Dashboard/Overview/OverviewDashboardPage.razor` (backs both `/dashboard/mentor` and `/dashboard/lecturer` via the same component, scoped by role server-side)

**Visible change**: **Yes — the most consequential of the epic.** This is the known-disagreement case from the original walkthrough audit: the same project scored Red by `ProjectHealthController` and "Healthy" by `LecturerDashboardController` in the same session. Migrating it means **a project's Healthy/Attention/AtRisk label can change** on the screen mentors and lecturers treat as ground truth every day. `MilestonesOverviewController`'s taxonomy also changes shape, not just value.

**Before/after verification**: for every project, list the current `LecturerDashboardController` label and score alongside the new `ProjectHealthService` status/delay for both `/dashboard/mentor` and `/dashboard/lecturer`. Every project whose label changes must be listed explicitly with the reason (e.g., "was 'Healthy' at score 82 because no milestone was overdue, but 3 open requests aged >7 days weren't visible in the score; is now Orange because the current milestone is 6 days late — the request backlog is now a separate, visible counter instead of hidden inside the old score"). Do the same for `MilestonesOverviewController`'s per-row status.

**Focused regression tests**
- New: a pinned regression test for the specific project the walkthrough audit found disagreeing between `ProjectHealthController` and `LecturerDashboardController` — asserts both now return the identical `Status`, with the old disagreement documented in a comment.
- New: cross-role snapshot test (the one flagged back in the original epic doc) — call the Health-exposing endpoints as Student, Mentor, and Staff for the same project, assert `Status`/`DelayDays` are byte-identical across all three. This is the direct regression test for the bug that started the whole audit.
- New: `MilestonesOverviewController` label-mapping test, once the mapping decision is made.

**Review checkpoint**: this slice needs two sign-offs, not one — first the label-mapping decision for `MilestonesOverviewController` (a product call, not a code review), then the full before/after project list for `LecturerDashboardController` reviewed as a lecturer and as a mentor, on `/dashboard/lecturer` and `/dashboard/mentor` respectively, before merging.

---

## Slice 6 — Remove deprecated calculations and dead helpers

**Scope**: no new service wiring — this slice only deletes what Slices 1–5 made unreachable. Runs only after all five are live and stable, not concurrently with any of them.

**To delete** (each only after confirming zero remaining references):
- `LecturerDashboardController`'s composite-score method and `HealthBuckets.FromScore`.
- `MentorController`'s own flat `MilestoneProgressPct`/`TaskProgressPct` calculation and its three inline overdue formulas.
- `MilestonesOverviewController`'s own per-row status derivation.
- `MentorRoadmapOverviewPage.CalcOverallPct`.
- `StudentDashboardHero._overallPct`.
- `ActionCenterCard`'s inline overdue + priority-ranking logic.
- `UpcomingSubmissionsCard`'s two overdue formulas.
- `TaskStatusHelpers.TaskRequiresAction` and the decision half of `GetDateDisplay` (its display-formatting half stays).
- The three separately-maintained `ResolveDisplayStatus`/`ResolveStatus` copies (`TaskStatusHelpers`, `UpcomingSubmissionsCard`, `ActionCenterCard`'s inline equivalent) — collapsed to zero, not reconciled into one more copy.
- Reads of the raw `Projects.HealthStatus` column in `ProjectsController`, `MentorController`, `ProjectOverviewController`, `ManagementController`. (Per the original epic, the **column itself** stays in the schema — dropping it is an explicit out-of-scope follow-up, not part of this slice.)

**Visible change**: **None expected**, by design — if Slices 1–5 fully replaced every consumer, this is pure dead-code removal. The risk isn't a visible change, it's an *invisible* one: deleting something a missed code path still quietly depended on. That risk, not user-facing behavior, is what this slice's checkpoint is designed to catch.

**Focused regression tests**: none new — this slice's job is to keep every test from Slices 1–5 green after deletion. Add one grep-based CI-style check per deleted symbol (`rg 'CalcOverallPct'` etc. returning zero matches) as a merge gate, so a future accidental reintroduction is caught immediately rather than at the next audit.

**Review checkpoint**: full regression suite green, then one complete re-run of the existing Playwright walkthrough (Dashboard → Tasks → Project Stages → Submissions → Requests, as Student/Mentor/Staff) from the original demo-story audit — not to find new inconsistencies, but to confirm none were introduced by the deletions. Then update `design/product-consistency-audit.md` marking every concept resolved, closing the epic.

---

## Which slices are visible, at a glance

| Slice | Screen(s) | Visible change? | Nature of the change |
|---|---|---|---|
| 1 — Project Stages | `/project-stages` | Minor, additive | Two progress numbers now explicitly labeled; no existing number changes |
| 2 — My Tasks | `/tasks` | **Yes** | Some tasks move between the three tiers (UTC-vs-local, override-awareness fixes) |
| 3 — Student Dashboard | `/dashboard` | **Yes, largest of the epic** | Hero % changes, Action Center top-3 membership can change, Upcoming Submissions overdue flags flip |
| 4 — Mentor views | `/mentor/projects`, `/mentor/projects/{id}`, `/mentor/roadmap` | **Yes** | Health badge appears for the first time; progress % may shift; the confirmed overdue self-contradiction is fixed |
| 5 — Lecturer/Staff views | `/dashboard/mentor`, `/dashboard/lecturer`, `/milestones-overview`, `/management/projects`, `/projects/{id}/overview` | **Yes, highest-risk of the epic** | Healthy/Attention/AtRisk labels can change on the primary staff/mentor KPI screen — the known-disagreement case |
| 6 — Cleanup | none (code only) | No | Pure deletion; risk is regression, not visible change |

Waiting for your review before starting Slice 1.
