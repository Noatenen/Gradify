# Business-Logic Consolidation Epic

Companion to `design/product-consistency-audit.md` (the audit that found this problem). That document catalogued every competing implementation; this one decides what survives, who owns it, and in what order it's safe to change. **Nothing here is implemented yet.** This is a plan awaiting approval, per instruction.

## Ground rules

- **No new calculation layer.** Every "canonical calculation" below is an existing implementation already in the codebase, promoted and relocated — never a freshly-invented formula competing with the ones it replaces.
- **One owning service per concept**, injected via the same DI pattern the codebase already uses (`AddScoped<T>()`, constructor-injected `DbRepository` — no EF Core, no new persistence pattern).
- **Behavior-preserving until explicitly stated otherwise.** Where consolidation would change a number a user currently sees (e.g., fixing the internal self-contradiction in `MentorController.GetProjectDetail`), that is called out explicitly as an intentional, reviewed change — never a silent side effect of refactoring.
- **Current-stage progress and whole-project progress are two different, permanently-named numbers.** Neither replaces the other.

---

## Concept 1 — Project Health

**Business meaning**: is this project's schedule slipping, and by how much? A single, explainable signal a mentor or lecturer can act on without needing to decode a formula.

**Canonical calculation**: the day-based delay model already in `ProjectHealthController` — per milestone, delay = days late (`CompletedAt − DueDate` if completed late, `today − DueDate` if still open and past due, floored at 0); project delay = the **max** over all milestones. `0 → Green`, `1–14 → Orange`, `>14 → Red`. This is the only one of the four existing implementations that answers the question in one explainable unit (days), so it survives as-is — no formula changes.

**Owning component**: new `Server/Services/ProjectHealthService.cs` — the exact body of `ProjectHealthController.GetAll`'s per-project computation, relocated out of the controller and exposed as `Task<ProjectHealthDto> GetProjectHealthAsync(int projectId)` and `Task<List<ProjectHealthDto>> GetProjectHealthBatchAsync(IEnumerable<int> projectIds)`. `ProjectHealthController` becomes a thin wrapper calling this service — the same relationship `RoadmapStagesController` already has with its own `BuildProgressAsync`, just extracted one level further so other controllers can call it too.

**DTO fields** (`ProjectHealthDto`, evolving the existing `ProjectHealthRowDto` shape, not replacing it):
- `ProjectId`, `Status` (`Green`/`Orange`/`Red`), `DelayDays`, `RelevantMilestoneTitle`, `RelevantMilestoneDueDate` — unchanged from today.
- New, additive, **separately labeled** fields: `OpenRequestCount`, `OldOpenRequestCount` (open >7 days), `MissingMandatorySubmissionCount` — the signals `LecturerDashboardController`'s composite score currently blends invisibly into one number. They ride alongside `Status`, never folded into it. A screen that wants "why is this Orange" shows these as separate stat chips, not as inputs to a hidden score.
- `MissingMandatorySubmissionCount` is **sourced from Concept 4's service**, not recomputed here — Health composes Urgency, it never re-derives it.

**Competing implementations found (to be deprecated)**:
| Where | What it does today |
|---|---|
| `ProjectHealthController.GetAll` inline computation | → becomes a thin caller of the new service (logic moves, doesn't change) |
| `LecturerDashboardController`'s `100 − overdue×10 − missing×8 − oldOpenReqs×5 − (currentMsOverdue?5:0)` score + `HealthBuckets.FromScore` | **Deprecated.** Replaced by calling the new service for `Status`/`DelayDays`, with the three extra counters exposed as separate fields (see above), not blended into a score. |
| Raw `Projects.HealthStatus` column, read by `ProjectsController`, `MentorController`, `ProjectOverviewController`, `ManagementController` | **Deprecated.** No writer was found anywhere in the codebase for this column — it should stop being read once every consumer calls the new service instead. The column itself can be dropped in a later cleanup pass (out of scope for this epic; flagged for a follow-up). |
| `MilestonesOverviewController`'s own `Overdue/NeedsAttention/Healthy` per-row status | **Deprecated.** Replaced by calling the new service. |

**Consumers to migrate**: `ProjectHealthController` (`/project-health`), `LecturerDashboardController` (`/dashboard/mentor`, `/dashboard/lecturer`), `MentorController` (`/mentor/projects`, `/mentor/projects/{id}`), `ManagementController` (`/management/projects`), `ProjectOverviewController` (`/projects/{id}/overview`), `MilestonesOverviewController` (`/milestones-overview`).

---

## Concept 2 — Project Progress (two explicitly distinct numbers)

**Business meaning — two separate questions, two separate names, always:**
- **Current-stage progress** (Hebrew: "התקדמות בשלב הנוכחי") — how much of the work *in the stage the project is on right now* is done. This is the 62%-style number.
- **Overall project progress** (Hebrew: "התקדמות כוללת בפרויקט") — how far along the *entire roadmap* is, averaged across every stage. This is the 52%-style number.

These must never appear on the same screen without a label distinguishing them, and no screen should present one when it means the other.

**Canonical calculations**:
- Current-stage progress: `Math.Round(100.0 × completedMilestones / totalMilestones)` for the one stage flagged `Current` — already `RoadmapStagesController.BuildProgressAsync`'s per-stage `ProgressPct`, unchanged.
- Overall project progress: `Math.Round(Average(stage.ProgressPct))` across stages with `LinkedMilestoneCount > 0` — already `MentorRoadmapOverviewPage.CalcOverallPct`'s formula, promoted from client-side to server-side so it's computed once instead of reinvented per page. This is not a new formula — it's relocating existing client logic into the same service that already produces the numbers it averages.

**Owning component**: new `Server/Services/ProjectRoadmapService.cs`, wrapping the exact existing `BuildProgressAsync` body (relocated from `RoadmapStagesController`, same reasoning as Concept 1) plus one addition: after building the `Stages` list, also compute and attach `OverallProjectProgressPct`. `RoadmapStagesController`'s three existing endpoints (`/my-progress`, `/projects/{id}/progress`, `/mentor/projects-progress`) become thin callers, exactly as they already are today for `BuildProgressAsync` — this part of the codebase is already the model to copy elsewhere.

**DTO fields** (extending `ProjectRoadmapProgressDto`, additive only):
- New top-level field: `CurrentStageProgressPct` (`int?`) — equal to `Stages.FirstOrDefault(s => s.Status == "Current")?.ProgressPct`, computed once so no consumer has to find the current stage in the list themselves and risk doing it differently.
- New top-level field: `OverallProjectProgressPct` (`int`) — the newly-server-side `CalcOverallPct` result.
- Existing `Stages[].ProgressPct`, `CurrentStageCode` unchanged.

**Competing implementations found (to be deprecated)**:
| Where | What it does today |
|---|---|
| `MentorRoadmapOverviewPage.CalcOverallPct` (client) | **Deprecated as client logic** — becomes a direct read of the new `OverallProjectProgressPct` field; the method is deleted, not reimplemented. |
| `ProjectOverviewController`'s `OverallProgressPercent` (task-completion ratio) | **Kept, but relabeled.** This measures something genuinely different — task throughput, not roadmap progress. It must be displayed as "השלמת משימות" (task completion) or similar, never as a substitute for either progress number above, and never on the same stat row without a distinguishing label. |
| `MentorController`'s `MilestoneProgressPct`/`TaskProgressPct` (flat whole-project milestone/task ratios) | **Deprecated.** Replaced by `OverallProjectProgressPct` from the shared service (for the milestone-based one) and the relabeled task-completion metric above (for the task-based one) — not merged into either. |
| `StudentDashboardHero._overallPct` (client, integer-truncated flat milestone count) | **Deprecated.** Replaced by reading `OverallProjectProgressPct` directly; truncation-vs-rounding discrepancy disappears because there's only one server-computed value now. |

**Consumers to migrate**: `RoadmapStagesController` (already the source, gains one field), `MentorRoadmapOverviewPage.razor`, `MentorController` (`/mentor/projects`, `/mentor/projects/{id}`), `StudentDashboardHero.razor`, `ProjectOverviewController` (relabel only, not replaced).

---

## Concept 3 — Current Stage

**Business meaning**: which point in the project's journey (Selection → Kickoff → Definition → Specification → Development → Evaluation → Submission/Grading) is the team on right now? This is a roadmap-level concept — distinct from "which individual milestone or task is next," which is a finer-grained, complementary question.

**Canonical calculation**: unchanged from `RoadmapStagesController.BuildProgressAsync` — first stage, by `DisplayOrder`, with ≥1 linked milestone that isn't 100% complete; every later stage is forced `Future` regardless of its own state, keeping the timeline monotonic. This is the only existing implementation that is stage-aware at all (the other four treat milestones as a flat, unstaged list), so it's the clear survivor.

**Owning component**: the same `ProjectRoadmapService` from Concept 2 — current stage and progress are computed together in one pass over the same stage/milestone data; splitting them into two services would itself be a form of duplication.

**DTO fields**: `CurrentStageCode` (existing), plus a new convenience field `CurrentStageName` (string?) so consumers don't need to cross-reference `Stages` by code to get a display-ready label.

A **separate, complementary** "what's the next actionable thing" concept (milestone- or task-level, not stage-level) currently has its own duplication — `ProjectsController.GetMyMilestones`'s `InProgress → Delayed → NotStarted` chain and `StudentDashboardHero`'s four-branch chain answer a genuinely different question than "current stage" and should **not** be merged into `ProjectRoadmapService`. They should be collapsed into **one** shared helper (not addressed by this epic's four concepts, but flagged here since the same audit surfaced it — recommend a fifth, smaller follow-up epic: "Next Actionable Milestone").

**Competing implementations found (to be deprecated)**:
| Where | What it does today |
|---|---|
| `MilestonesOverviewController`'s flat first-non-completed-by-`OrderIndex` pick | **Deprecated.** Replaced by `CurrentStageName`/`CurrentStageCode` from the shared service (screen may additionally want the next *milestone* within that stage — see the flagged follow-up above). |
| `MentorController`'s near-identical flat pick | **Deprecated.** Same replacement. |

**Consumers to migrate**: `MilestonesOverviewController` (`/milestones-overview`), `MentorController` (`/mentor/projects`).

---

## Concept 4 — Task Urgency / Requires Attention

**Business meaning**: does this specific task need a human to do something about it right now, and if several tasks qualify, which is most important? This is the concept every "Requires Attention" / "Upcoming Submissions" / My-Tasks-tier surface is really asking about.

**Canonical calculation**: two existing implementations, kept as two named signals — not merged into one boolean, because they answer different questions:
- **`IsOverdue`** — promoted from `ProjectsController.GetMyTasks`'s `IsUrgent` SQL flag (the most rigorous of the ten formulas found: mandatory, no existing submission, past the *effective* due date after per-team overrides, not already in a terminal state). This is the one already-correct implementation — it was computed, sent to the client, and then ignored; this epic makes it actually used everywhere instead of writing a better one.
- **`AttentionReason`** — a canonical enum (`None`, `ReturnedForRevision`, `Overdue`, `PendingMentorReview`, `PendingMoodleConfirmation`, `DueSoon`), replacing the three separately-maintained copies of `ResolveDisplayStatus`/`ResolveStatus` and the ranking order already agreed on in earlier work (`Returned > Overdue > PendingMoodle > Request`), extended consistently.

Both are computed **server-side, in UTC, once**. This permanently retires the UTC-vs-browser-local risk the audit flagged — the client displays what the server decided and never recomputes "is this overdue" itself again.

**Owning component**: new `Server/Services/TaskUrgencyService.cs` — `Task<List<TaskUrgencyDto>> GetTaskUrgencyForProjectAsync(int projectId)` (bulk, since every real consumer needs it for many tasks at once) and a single-task variant for detail views.

**DTO fields** (`TaskUrgencyDto`):
- `TaskId`, `IsOverdue` (bool), `AttentionReason` (string enum), `AttentionRank` (int, for sorting — lower is more urgent, matching the existing `Returned=0, Overdue=1, PendingMoodle=2, Request=3` convention already used by `ActionCenterCard` today).

**Competing implementations found (to be deprecated)** — this is the largest cluster from the audit, ten formulas total:
| Where | Fate |
|---|---|
| `MentorController.GetProjects` SQL overdue count | Deprecated — replaced by a `Count(IsOverdue)` over the new service's bulk result. |
| `MentorController.GetProjectDetail`'s two internally-disagreeing overdue computations | Deprecated — this also **fixes** the self-contradiction the audit found (a task flagged overdue on its own row but excluded from the page's own aggregate count), since both numbers now come from the same call. |
| `ProjectsController.GetMyTasks`'s `IsUrgent` SQL | **Promoted**, relocated into the new service, not deleted — this is the surviving implementation. |
| `ProjectsController.GetMyTasks`'s `IsNeedsAttentionStatus` | Folded into `AttentionReason`'s enum values (`ReturnedForRevision`/`SubmittedToMentor`/`RevisionSubmitted` map onto existing reasons) rather than kept as a separate boolean. |
| `ActionCenterCard`'s inline overdue + priority logic | Deprecated — replaced by reading `AttentionReason`/`AttentionRank` directly, sorted and capped in the component's markup only, no re-derivation. |
| `UpcomingSubmissionsCard`'s two overdue formulas | Deprecated — replaced by reading `IsOverdue` directly. |
| `TaskStatusHelpers.TaskRequiresAction` | Deprecated — replaced by a simple check against `AttentionReason != None`. |
| `TaskStatusHelpers.ResolveDisplayStatus`, `UpcomingSubmissionsCard.ResolveStatus`, `ActionCenterCard`'s inline equivalent | Collapsed into the single server-side `AttentionReason` — the three client-side copies are deleted, not reconciled. |
| `TaskStatusHelpers.GetDateDisplay`'s overdue check | Kept for its *display formatting* (days-ago text), but the overdue *decision* it currently makes independently is replaced by reading `IsOverdue`. |

**Consumers to migrate**: `MentorController` (both endpoints), `ProjectsController.GetMyTasks`, `ActionCenterCard.razor`, `UpcomingSubmissionsCard.razor`, `StudentTasksPage.razor` + `TaskStatusHelpers.cs`, and (via Concept 1's dependency) `ProjectHealthService`'s `MissingMandatorySubmissionCount`.

---

## Dependency graph (why the phase order below is what it is)

```
TaskUrgencyService  ─┬─▶  ProjectHealthService (MissingMandatorySubmissionCount)
                      └─▶  ActionCenterCard / UpcomingSubmissionsCard / StudentTasksPage

ProjectRoadmapService ──▶  ProjectHealthController's "relevant milestone" display (informational only, no hard dependency)
```
`TaskUrgencyService` has no dependency on the other three — it's the safest, most self-contained place to start.

---

## Phased migration plan

Each phase is independently reviewable and independently shippable — no phase leaves the app in a broken state, and every phase before Phase 4 changes zero user-visible behavior.

**Phase 0 — Alignment (this document).** Agree on the four canonical calculations, the owning services, and the DTO field names before any code is written.

**Phase 1 — Extract, don't wire.** Create `TaskUrgencyService`, `ProjectHealthService`, `ProjectRoadmapService` as new files containing the exact bodies of the implementations chosen as canonical above, verbatim, moved out of their current controllers. Add the new DTO fields (`OverallProjectProgressPct`, `CurrentStageProgressPct`, `CurrentStageName`, `TaskUrgencyDto`). Register the services in `Program.cs`. **Nothing calls them yet** — old controller code stays exactly as it is, running side by side. Zero behavior change; purely additive. Reviewable as pure new-code addition with no risk to anything currently working.

**Phase 2 — Migrate server-side consumers, one controller at a time**, each as its own small, reviewable change:
1. `RoadmapStagesController` → calls `ProjectRoadmapService` (its own logic, just relocated — verify identical output first).
2. `ProjectHealthController` → calls `ProjectHealthService` (same).
3. `MentorController` (both endpoints) → calls `TaskUrgencyService` + `ProjectHealthService` + `ProjectRoadmapService`. This is where the self-contradicting overdue count gets fixed — flagged explicitly in the PR/commit as an intentional behavior change, with before/after numbers shown.
4. `LecturerDashboardController` → calls `ProjectHealthService` for `Status`/`DelayDays` instead of computing its own score; `OpenRequestCount`/`MissingMandatorySubmissionCount` become separate displayed fields instead of score inputs. Flagged as an intentional behavior change (a project's "Healthy/Attention/AtRisk" label may change for projects where the two formulas previously disagreed — expected, and the point of this whole epic).
5. `ManagementController`, `MilestonesOverviewController`, `ProjectOverviewController` → same pattern.

Each of these five is a separate, small, revertible change touching one controller.

**Phase 3 — Migrate client consumers**, again one component at a time:
1. `ActionCenterCard.razor` → reads `AttentionReason`/`AttentionRank`, deletes its inline logic.
2. `UpcomingSubmissionsCard.razor` → reads `IsOverdue`, deletes its two inline formulas.
3. `StudentTasksPage.razor` + `TaskStatusHelpers.cs` → reads `AttentionReason`, deletes `TaskRequiresAction`/`ResolveDisplayStatus`'s decision logic (keeps only display formatting).
4. `StudentDashboardHero.razor` → reads `OverallProjectProgressPct`/`CurrentStageProgressPct`, deletes `_overallPct`.
5. `MentorRoadmapOverviewPage.razor` → reads `OverallProjectProgressPct`, deletes `CalcOverallPct`.

This is the phase where a user might see a number change (e.g., a task that was wrongly "not overdue" under the old browser-local check now correctly shows as overdue) — each component's migration should ship with a screenshot/before-after note.

**Phase 4 — Remove the deprecated implementations** listed in each concept section above, once nothing references them. Includes deciding the fate of the unused `Projects.HealthStatus` column (recommend: stop reading it in this epic, drop it in a later, separate migration once confirmed nothing else depends on it).

**Phase 5 — Cross-role verification** (see tests below) and update `design/product-consistency-audit.md` to mark each concept resolved.

---

## Tests required to prove Student, Mentor, and Staff see the same reality

These are **new tests this epic must add**, not existing ones to preserve — the whole point is that no such tests exist today, which is how the divergence went unnoticed:

1. **Cross-role snapshot test**: seed one project (reuse the demo dataset), then call all endpoints that expose Health/Progress/Current-Stage/Urgency for that same project as Student, Mentor, and Staff — assert `Status`, `DelayDays`, `CurrentStageCode`, `CurrentStageProgressPct`, `OverallProjectProgressPct`, and every task's `IsOverdue`/`AttentionReason` are byte-identical across all three role-perspectives. This is the direct regression test for the original bug.
2. **`ProjectHealthService` unit tests**: known milestone due-date/completion fixtures → expected `Status`/`DelayDays`, covering the boundary values (exactly 0, exactly 14, exactly 15 days).
3. **`ProjectRoadmapService` unit tests**: fixture stage/milestone sets → expected `CurrentStageCode`, `CurrentStageProgressPct`, `OverallProjectProgressPct` — including the edge case of zero stages with linked milestones (should not divide by zero or return a misleading 0%).
4. **`TaskUrgencyService` unit tests**: fixture tasks covering every `AttentionReason` value, plus the specific per-team due-date-override case (the one input the old, deprecated formulas most often got wrong), plus explicit UTC-boundary tests (a task due "today" at 23:59 UTC vs. 00:01 UTC) to close the timezone gap for good.
5. **Regression fixtures for the two intentional behavior changes** (Phase 2 steps 3 and 4 above) — a test asserting the *new* correct numbers for the specific projects where the audit found disagreement, with the old/wrong numbers documented in the test's comment so the fix is traceable.
6. **A demo-data-driven manual QA pass**: re-run the existing Playwright walkthrough (Dashboard → Tasks → Project Stages → Submissions → Requests, as Noa/Ofir/Yanai/Jenny) already used in the demo-story audit, confirming every screen now shows the same Health/Progress/Current-Stage/Urgency facts for the same project.

---

## What's explicitly out of scope for this epic

- The "Next Actionable Milestone" duplication flagged under Concept 3 (a real, separate finding — recommend its own small follow-up epic, not folded in here).
- Dropping the `Projects.HealthStatus` column from the schema (stop reading it here; drop it later once confirmed safe).
- Any UI/visual redesign — this epic is purely about consolidating the calculations DTOs expose; the Dashboard V2 redesign resumes only after this is merged, built on the now-consistent fields.

Waiting for approval before writing any code.
