# Motiva Product Consistency Audit

Commissioned after the demo-data walkthrough surfaced two controllers disagreeing about the same project's health. This audit exists to answer one question before any further redesign work: **for each computed concept in the product, how many independent implementations exist, and which one should survive?**

Method: four parallel research passes read every controller and every client component that computes anything resembling health, progress, current stage, overdue, or attention — with exact code, not paraphrase. Findings below are traced to file and line. No code has been changed. This is a decision document, not a patch.

## Headline finding

**Every single concept audited has at least three independent implementations. None has a true single source of truth today.** The two-controllers-disagree bug that started this audit wasn't an isolated incident — it's the general condition of the codebase. Health has four implementations. Progress-percentage has five. "Is this overdue / does it need attention" has ten. This is why the redesign shouldn't proceed against the current business logic as-is: any new screen built today would have to arbitrarily pick one of several already-competing answers, and would likely become an eleventh.

---

## 1. Project Health

| # | Where calculated | Formula | Screens |
|---|---|---|---|
| 1 | `ProjectHealthController.GetAll` (`Server/Controllers/ProjectHealthController.cs:36-197`) | Per-milestone delay = days late (completed-late: `CompletedAt − DueDate`; still-open: `today − DueDate`), **max over all milestones**. `0 → Green`, `1–14 → Orange`, `>14 → Red`. Milestones only — no tasks, no requests. | `/project-health` |
| 2 | `LecturerDashboardController.GetOverview` (`Server/Controllers/LecturerDashboardController.cs:32-345`) | Point-deduction score: `100 − overdue×10 − missing×8 − oldOpenReqs×5 − (currentMilestoneOverdue ? 5 : 0)`, clamped 0–100. `≥80 Healthy`, `≥60 Attention`, `else AtRisk`. Folds in Tasks, TaskSubmissions, and ProjectRequests — none of which #1 touches. | `/dashboard/lecturer`, `/dashboard/mentor` (same controller action, server-enforced scope) |
| 3 | Raw `Projects.HealthStatus` column, read verbatim (never derived) by `ProjectsController.cs:40,273`, `MentorController.cs:28,111,304`, `ProjectOverviewController.cs:49,253`, `ManagementController.cs:46` | Whatever was last written to this column — **no controller in the codebase was found writing it**, so its value's provenance is currently unknown/stale by construction. | `/mentor/projects`, `/management/projects`, `/projects/{id}/overview` |
| 4 | `MilestonesOverviewController` (`Server/Controllers/MilestonesOverviewController.cs:175-215`) | Per-project-row status: `Overdue` (current milestone past due) → else `NeedsAttention` (if any missing submission) → else `Healthy`. Uses `DateTime.Today` (server-local), not UTC. | `/milestones-overview` |

**Duplicates confirmed**: yes — four, not two. #1 and #2 were the ones already known to disagree (verified live: the same project scored Red by #1, "Healthy" by #2, in the same session). #3 is arguably the most concerning: a column four different screens display as fact, with no code path anywhere populating it — meaning whatever a viewer sees there is either a leftover manual value or permanently blank/default, and none of the other three real computations feed it.

**Recommended single source of truth**: **#1's milestone-delay model** (`ProjectHealthController`'s), because it's the only one that answers the question a health indicator should answer — "is the schedule slipping, and by how much" — with a concrete, explainable unit (days late). #2's composite score conflates schedule slippage with unrelated things (open requests, missing submissions) into one number that can't be unpacked by looking at it. Recommendation: keep day-based delay as the canonical health computation; if request-backlog and missing-submissions matter for a lecturer's triage view, surface them as **separate, labeled signals** next to health, not folded into one opaque score. Retire #3 (delete the column or make one real computation the sole writer of it) and #4 (fold into whichever screen `/milestones-overview` becomes, using the canonical computation).

---

## 2. Progress % (project/stage completion)

| # | Where calculated | Formula | Unit | Screens |
|---|---|---|---|---|
| 1 | `RoadmapStagesController.BuildProgressAsync` (`Server/Controllers/RoadmapStagesController.cs:456-623`) | `Math.Round(100.0 × completedMilestones / totalMilestones)`, **per stage** | Milestones, stage-scoped | `/project-stages` (student), `/mentor/roadmap`, `/mentor/projects/{id}` — single private method, three public endpoints all call it identically, no divergence between them |
| 2 | `MentorRoadmapOverviewPage.CalcOverallPct` (client, `Client/Pages/Mentor/MentorRoadmapOverviewPage.razor:222-233`) | `Math.Round(Average(stage.ProgressPct))` across stages with linked milestones | Average-of-#1's-own-numbers, a derived client metric with no server equivalent | `/mentor/roadmap` (same page as #1, different number) |
| 3 | `ProjectOverviewController.GetOverview` (`Server/Controllers/ProjectOverviewController.cs:224-235`) | `Math.Round(100.0 × completedTasks / totalTasks)`, **whole project** | Tasks, not milestones; "completed" = `Done/Completed/SubmittedToMentor` **or has any submission at all** (regardless of mentor approval) | `/projects/{id}/overview` |
| 4 | `MentorController.GetProjects`/`GetProjectDetail` (`Server/Controllers/MentorController.cs:120-122,310-311`) | `completedMilestones × 100 / totalMilestones` (list) — same shape recomputed independently in the detail endpoint; detail endpoint also adds `TaskProgressPct = completedTasks × 100 / totalTasks` | Milestones, whole project (not stage-scoped, unlike #1) | `/mentor/projects`, `/mentor/projects/{id}` |
| 5 | `StudentDashboardHero` (client, `Client/Pages/Dashboard/StudentDashboardHero.razor:192-193`) | `_completedCount × 100 / Milestones.Count` — **integer division, no rounding** (truncates), whole project | Milestones, whole project | `/dashboard` (student) |

**Duplicates confirmed**: five, spanning at least three different *units* (stage-scoped milestone-ratio, whole-project milestone-ratio, whole-project task-ratio) and two different rounding behaviors (`Math.Round` vs. truncation). This is exactly the "52% vs 62%" finding from the demo audit — both numbers were individually correct by their own formula, for the same project, on two panels of the same page.

**Recommended single source of truth**: **#1, `RoadmapStagesController.BuildProgressAsync`**, for anything claiming to show "project progress" tied to the roadmap/stage model — it's already the one place with zero internal divergence across its three callers. `ProjectOverviewController`'s task-ratio (#3) measures something genuinely different (raw task throughput) and can coexist **only if relabeled** to say what it actually is ("task completion," not "project progress"), never presented as interchangeable with #1's number. #2, #4, and #5 should be deleted and replaced with direct consumption of #1's per-stage `ProgressPct` (or, if a single "how far along overall" number is genuinely wanted, one new explicit aggregate added to `BuildProgressAsync`'s own DTO — computed once, server-side — rather than three different client/controller pages each inventing their own average).

---

## 3. Current Stage / Current Milestone

| # | Where calculated | Rule | Screens |
|---|---|---|---|
| 1 | `RoadmapStagesController.BuildProgressAsync` | First stage (by `DisplayOrder`) with ≥1 linked milestone that isn't 100% complete becomes `Current`; every later stage is forced `Future` regardless of its own state ("monotonic timeline" by design) | `/project-stages`, `/mentor/roadmap`, sidebar's persistent stage widget |
| 2 | `MilestonesOverviewController.GetOverview` (`Server/Controllers/MilestonesOverviewController.cs:156-164`) | First **milestone** (not stage) that isn't `Completed`, by `OrderIndex`, project-wide flat list; falls back to the last milestone if all are complete | `/milestones-overview` |
| 3 | `MentorController.GetProjects` (`Server/Controllers/MentorController.cs:44-67`) | First milestone by `OrderIndex` with `Status NOT IN (Completed, Done)` — a third, separately-written variant of the same idea as #2 | `/mentor/projects` |
| 4 | `ProjectsController.GetMyMilestones` (`Server/Controllers/ProjectsController.cs:515-517`) | Priority chain: `InProgress → Delayed → NotStarted` | `/tasks` (student My Tasks page's "current milestone" header) |
| 5 | `StudentDashboardHero` (client, `Client/Pages/Dashboard/StudentDashboardHero.razor:186-190`) | Four-branch chain: `IsCurrentlyOpen && InProgress` → `IsCurrentlyOpen` → `InProgress` → `Status != Completed` — duplicated verbatim in the sibling `ProjectJourneyCard.razor` | `/dashboard` (student) |

**Duplicates confirmed**: five distinct selection rules, three of which (#2, #3, #4) are subtly different re-implementations of "first not-done thing in order," none sharing code, and #1 is the only one aware of the **stage** concept at all — the other four operate on a flat milestone list and have no notion that stages exist.

**Recommended single source of truth**: **#1's stage-current derivation** for anything about "where is this project in its journey" — it's the one place the concept is actually modeled correctly (monotonic, stage-aware). #4/#5's milestone-level "what's the next active thing" is a legitimately different, complementary question ("what should I click next" vs. "what stage am I in") and can stay as its own concept — but it should be **one** implementation, not two independently-coded chains that happen to look similar. Recommend collapsing #4 and #5 into one shared helper (used by both the student Tasks page and the Dashboard hero), and deleting #2/#3 in favor of calling #1 (or the milestone-level helper) directly once `/milestones-overview` and `/mentor/projects` are updated to consume it.

---

## 4. Overdue Tasks

Ten distinct formulas were found — the single largest cluster of duplication in the system.

| # | Where | Formula | Time basis | Screens |
|---|---|---|---|---|
| 1 | `MentorController.GetProjects` SQL | `DueDate < datetime('now') AND Status NOT IN (Done,Completed,ApprovedForSubmission)` | SQLite `datetime('now')`, UTC w/ time | `/mentor/projects` |
| 2 | `MentorController.GetProjectDetail` (aggregate) | Same exclusion list as #1, recomputed in C# from a separately-fetched row set | `DateTime.UtcNow` | `/mentor/projects/{id}` |
| 3 | `MentorController.GetProjectDetail` (per-task `IsOverdue`) | **Narrower** exclusion (`Done, ApprovedForSubmission` only — omits `Completed`) than #2, on the *same page* | `DateTime.UtcNow` | `/mentor/projects/{id}` — internally inconsistent with its own #2 |
| 4 | `ProjectsController.GetMyTasks` SQL (`IsUrgent`) | `IsMandatory=1 AND Status NOT IN (Done,Completed,SubmittedToMentor) AND ClosedAt IS NULL AND no TaskSubmissions row exists AND date(EffectiveDueDate) < date('now')` — the most rigorous formula found, and the only one respecting per-team due-date overrides | SQLite `date('now')`, date-only, UTC | Computed for `/tasks` but **never read by the page that requests it** (see below) |
| 5 | `ProjectsController.GetMyTasks` (`IsNeedsAttentionStatus`) | Pure status check (`ReturnedForRevision/SubmittedToMentor/RevisionSubmitted`), no date at all | n/a | `/tasks` summary counters |
| 6 | `ActionCenterCard` (client) | `DueDate.Date < DateTime.Today && !IsComplete(task) && Status IN (Open,InProgress,ReturnedForRevision)` | **Browser-local** `DateTime.Today` | `/dashboard` |
| 7 | `UpcomingSubmissionsCard` (client, primary list) | `DueDate.Date < DateTime.Today`, no status check at all | Browser-local | `/dashboard` |
| 8 | `UpcomingSubmissionsCard` (fallback) | `(NextDeadline.DueDate.Date − DateTime.Today).TotalDays < 0` | Browser-local | `/dashboard` |
| 9 | `TaskStatusHelpers.TaskRequiresAction` | Status checks, or `DueDate.Date < DateTime.Today` (overdue), or due within 3 days | Browser-local | `/tasks` (My Tasks page tiers) |
| 10 | `TaskStatusHelpers.GetDateDisplay` | `(DueDate.Date − DateTime.Today).Days < 0` | Browser-local | `/tasks` (date labels) |

The single most important line in all four research passes: **`TaskItemDto.IsUrgent` (#4) — the one server-computed overdue flag that actually respects per-team due-date overrides and excludes tasks that already have a submission attached — is never read anywhere on the client.** `grep -rn "\.IsUrgent\b" Client/` returns zero matches outside of an unrelated CSS-styling parameter of the same name. The My Tasks page computes its own answer (#9) from scratch instead, ignoring the more rigorous flag the server already sent it.

**Duplicates confirmed**: ten, with a genuine internal contradiction inside a single endpoint (#2 vs. #3) and a fully-computed-but-discarded correct answer (#4). Also a real, confirmed **UTC-vs-local** risk: every client-side formula (#6–10) uses the browser's local `DateTime.Today`, while every server-side formula (#1–4) evaluates in UTC — for any user not in a UTC-aligned timezone, a task can read "overdue" on one screen and "due today" on another purely from this mismatch, independent of any logic difference.

**Recommended single source of truth**: **`IsUrgent` (#4)**, extended to be *the* mandatory-vs-optional-aware, override-aware, submission-aware overdue flag, computed once server-side and consumed everywhere — student Tasks page, Dashboard cards, and mentor/lecturer overdue counts alike. This retires #1, #2, #3, #6, #7, #8, #9's date-based branch, and #10 in one move; #5 (`IsNeedsAttentionStatus`) measures a genuinely different thing ("needs a human response regardless of date") and can stay as a second, clearly-named flag alongside `IsUrgent`, not merged into it.

---

## 5. Requires Attention / Upcoming Submissions

These are presentation-layer groupings built **on top of** the overdue/status primitives above, so they inherit every inconsistency already listed — but they also add their own duplication in how they rank and dedupe:

- `ActionCenterCard` ("דורש התייחסות") ranks `Returned > Overdue > PendingMoodle > Request`, capped at 3 — a client-only priority order that exists nowhere else.
- `UpcomingSubmissionsCard` ("הגשות קרובות") independently re-derives its own submission list and overdue flag (formulas #7/#8 above), duplicating rather than reusing `ActionCenterCard`'s.
- `StudentTasksPage`'s three tiers (`ActionRequiredTasks`/`WaitingOnOthersTasks`/`UpcomingTasks`) use `TaskStatusHelpers` (formula #9), a third independent priority scheme.
- `ResolveDisplayStatus`, the status-priority mapping feeding #9, has **three separately-maintained near-duplicate copies**: `TaskStatusHelpers.ResolveDisplayStatus`, `UpcomingSubmissionsCard.ResolveStatus`, and `ActionCenterCard`'s inline equivalent checks.

**Recommended single source of truth**: one shared status-priority helper (consolidating the three `ResolveDisplayStatus`/`ResolveStatus` copies) and one shared "what needs attention, ranked" function built on top of the unified `IsUrgent` flag from §4 — consumed identically by the Dashboard's attention card and the Tasks page's tiers, so the same task in the same state produces the same verdict on both screens. This is directly relevant to the Dashboard V2 design already in progress (Today's Focus + My Tasks should provably share one ranking function, not two that happen to agree today by coincidence).

---

## 6. Dashboard summaries by role — screen inventory

This is the full map of every summary/KPI screen in the product and which of the above implementations each one actually consumes — the fragmentation made concrete.

| Screen | Role | Backing controller | Health impl. | Progress impl. | Overdue impl. |
|---|---|---|---|---|---|
| `/dashboard` | Student | `ProjectsController.GetMyDashboard` | — | #5 (Hero) | #6, #7, #8 (client cards) |
| `/tasks` | Student | `ProjectsController.GetMyTasks` | — | — | #4 (computed, unused), #5, #9, #10 |
| `/project-stages` | Student | `RoadmapStagesController.GetMyProgress` | — | #1 | — |
| `/dashboard/mentor` | Mentor | `LecturerDashboardController.GetOverview` (scope=mentor) | #2 | (current-milestone #3-style) | #1-family (aggregate) |
| `/mentor/projects` | Mentor | `MentorController.GetProjects` | #3 (raw column) | #4 | #1 |
| `/mentor/projects/{id}` | Mentor | `MentorController.GetProjectDetail` | #3 | #4 | #2 **and** #3 (self-contradicting) |
| `/mentor/roadmap` | Mentor | `RoadmapStagesController.GetMentorProjectsProgressAsync` + client `CalcOverallPct` | — | #1 **and** #2 on the same page | — |
| `/project-health` | Mentor/Staff | `ProjectHealthController.GetAll` | #1 | — | (feeds health only) |
| `/dashboard/lecturer` | Staff | `LecturerDashboardController.GetOverview` (scope=lecturer) | #2 | (current-milestone #3-style) | #1-family (aggregate) |
| `/management/projects` | Staff | `ManagementController.GetProjects` | #3 (raw column, never written) | — | — |
| `/milestones-overview` | Staff/Mentor | `MilestonesOverviewController.GetOverview` | #4 | (current-milestone #2-style) | own (status-only, `DateTime.Today`) |
| `/projects/{id}/overview` | Staff/Mentor | `ProjectOverviewController.GetOverview` | #3 (raw column) | #3 (task-ratio) | own aggregate |

Reading across any single row of this table for one real project (as the live audit did) can legitimately produce a different health verdict, a different progress percentage, and a different overdue count depending purely on which screen happens to be open — not because the underlying facts changed, but because each screen asked a differently-coded question.

---

## Recommendation for how to proceed

Per your instruction, this document stops at "which one should become the single source of truth" — no code has been written. Suggested next step, in order:

1. Agree on the four canonical computations named above (§1 health, §2 progress, §3 current-stage, §4 overdue) as the only ones that should exist going forward.
2. Decide where they should live — most naturally as shared server-side query/service methods that every controller (mentor, lecturer, staff, student) calls identically, the same pattern `RoadmapStagesController.BuildProgressAsync` already proves works (one method, three callers, zero divergence).
3. Only then resume the Dashboard V2 work, built against these consolidated concepts from the start rather than inheriting an eleventh implementation.

This is a genuinely larger undertaking than the demo-data fixes — it touches eight controllers and roughly a dozen client components — so it likely deserves its own scoped effort rather than being folded into the Dashboard redesign as a side effect.
