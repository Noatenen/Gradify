# Phase 2 Migration Dependency Map

Companion to `design/product-consistency-audit.md` (what's duplicated) and `design/business-logic-consolidation-epic.md` (what should survive, Phase 1 of which is already merged: `ProjectHealthService`, `ProjectRoadmapService`, `TaskUrgencyService` exist and are DI-registered, but nothing calls them yet).

This document answers one question before Phase 2 starts: **in what order do we wire the new services in, and which wirings are trivial vs. which will change what a real user sees?** No code is changed by this document.

## How to read the trees

Each canonical service fans out to the controllers that currently compute its concept independently, which fan out to the client components that render it, which fan out to the URL a person actually opens. A single controller/component often appears under more than one service — that's expected, since e.g. `MentorController` currently hand-rolls its own health, progress, *and* overdue logic in one file.

**Classification key**
- 🟢 **Safe migration** — either a verbatim relocation of code already trusted (zero formula change), or a pure label/text change. Lowest review burden; a diff + one output comparison should be enough.
- 🟡 **Requires behavioral review** — the new number can differ from today's for real data (different formula, different time basis, or a previously-empty field becoming populated). Needs an explicit before/after comparison on real projects before merging, but the blast radius is contained to one controller or one card.
- 🔴 **High-risk migration** — high-traffic screen, and/or the change restructures ranking/grouping (not just a single number), and/or it's the exact screen where the original bug was first spotted. Needs a side-by-side screenshot/data comparison, should ship behind its own reviewable PR, and should not be batched with any other migration.

---

## 1. ProjectHealthService

```
ProjectHealthService
    │
    ├─▶ ProjectHealthController                         🟢 Safe
    │       └─▶ /project-health                          (Mentor/Staff)
    │
    ├─▶ ProjectOverviewController (raw HealthStatus col)  🟡 Requires review
    │       └─▶ /projects/{id}/overview                  (Staff/Mentor)
    │
    ├─▶ ManagementController (raw HealthStatus col)       🟡 Requires review
    │       └─▶ /management/projects                     (Staff)
    │
    ├─▶ MentorController (raw HealthStatus col)           🟡 Requires review
    │       └─▶ /mentor/projects, /mentor/projects/{id}  (Mentor)
    │
    ├─▶ MilestonesOverviewController (own 3-value status) 🟡 Requires review
    │       └─▶ /milestones-overview                     (Staff/Mentor)
    │
    └─▶ LecturerDashboardController (composite score)     🔴 High-risk
            └─▶ /dashboard/mentor, /dashboard/lecturer    (Mentor, Staff)
```

**Why each rating:**
- `ProjectHealthController` is where the canonical logic was extracted *from* — calling the service instead of the inline copy is a no-op by construction.
- `ProjectOverviewController` / `ManagementController` / `MentorController` currently read `Projects.HealthStatus`, a column **nobody writes** (confirmed in the audit). Today's badge is either stale or blank; swapping in real computed values is very likely a strict improvement, but it means these screens will show a Health badge for the first time in practice, so a reviewer needs to actually look at the rendered page, not just the diff.
- `MilestonesOverviewController` uses a different three-value taxonomy (`Overdue`/`NeedsAttention`/`Healthy`) built from server-local `DateTime.Today` and a different rule entirely (not day-based delay). Mapping it onto Green/Orange/Red changes both the label set and the underlying logic — needs a product decision on label mapping, not just a code swap.
- `LecturerDashboardController` is the **known disagreement case** — its composite score already contradicts `ProjectHealthController` for real projects (verified live in the walkthrough audit). Migrating it will *change the Healthy/Attention/AtRisk label mentors and staff see on their main dashboard*, for exactly the projects where the two formulas disagree today. This is the highest-traffic, most-trusted-as-fact screen of the six, so it's rated 🔴 and should migrate last, with an explicit before/after list of which projects' labels change and why.

---

## 2. ProjectRoadmapService

```
ProjectRoadmapService
    │
    ├─▶ RoadmapStagesController                           🟢 Safe
    │       ├─▶ /project-stages                            (Student)
    │       ├─▶ /mentor/roadmap                             (Mentor)
    │       └─▶ /mentor/projects/{id} (progress panel)       (Mentor)
    │
    ├─▶ MentorRoadmapOverviewPage.razor (CalcOverallPct)    🟢 Safe
    │       └─▶ /mentor/roadmap                             (Mentor)
    │
    ├─▶ ProjectOverviewController (relabel only, kept)      🟢 Safe
    │       └─▶ /projects/{id}/overview                      (Staff/Mentor)
    │
    ├─▶ MentorController (flat MilestoneProgressPct/        🟡 Requires review
    │       TaskProgressPct → OverallProjectProgressPct)
    │       └─▶ /mentor/projects, /mentor/projects/{id}      (Mentor)
    │
    └─▶ StudentDashboardHero.razor (_overallPct,            🔴 High-risk
            truncation → OverallProjectProgressPct/
            CurrentStageProgressPct)
            └─▶ /dashboard                                  (Student)
```

**Why each rating:**
- `RoadmapStagesController` is the source the service body was copied from verbatim — already proven zero-divergence across its own three callers. Trivial.
- `MentorRoadmapOverviewPage.CalcOverallPct` is being *deleted*, not reimplemented — it's replaced by reading the server field that computes the exact same average, just once instead of per-page-load. One side-by-side check is enough.
- `ProjectOverviewController`'s task-completion ratio isn't being replaced at all, only relabeled in the UI so it stops looking like a progress number — no calculation touched.
- `MentorController`'s flat milestone/task ratios are **not stage-aware** today (unlike the canonical calculation), so real projects can show a different number after migration purely because the formula changes shape (flat ratio → stage-weighted average). Needs a before/after pass on real mentor-facing projects.
- `StudentDashboardHero` is rated 🔴 deliberately: it's the exact component where the original "62% vs 52%" discrepancy was first spotted in the walkthrough audit, it's integer-truncating today (not rounding), and it's the single most-viewed screen in the product (student Dashboard, every login). Any change here is the one most likely to generate a support question from a student who remembers their old percentage. Migrate last, with the current and new number captured side-by-side for a handful of real demo projects before merging.

---

## 3. TaskUrgencyService

```
TaskUrgencyService
    │
    ├─▶ ProjectsController.GetMyTasks (IsUrgent SQL)        🟢 Safe
    │       └─▶ /tasks                                       (Student)
    │
    ├─▶ MentorController (3 overdue formulas,                🟡 Requires review
    │       incl. the self-contradicting pair)
    │       └─▶ /mentor/projects, /mentor/projects/{id}       (Mentor)
    │
    ├─▶ UpcomingSubmissionsCard.razor (2 browser-local        🟡 Requires review
    │       overdue formulas → IsOverdue)
    │       └─▶ /dashboard                                    (Student)
    │
    ├─▶ ActionCenterCard.razor (inline overdue + priority     🔴 High-risk
    │       order → AttentionReason/AttentionRank)
    │       └─▶ /dashboard                                    (Student)
    │
    └─▶ StudentTasksPage.razor + TaskStatusHelpers.cs         🔴 High-risk
            (3-tier grouping + 3 duplicate ResolveDisplayStatus
            copies → AttentionReason)
            └─▶ /tasks                                        (Student)
```

Plus one internal (non-screen) edge: **TaskUrgencyService → ProjectHealthService**, which composes `MissingMandatorySubmissionCount` from Urgency rather than recomputing it. This has no screen of its own — it's a dependency ordering constraint, not a consumer to review. 🟢 Safe, but it does mean `ProjectHealthService`'s enrichment fields can't be finalized until `TaskUrgencyService` is wired into at least one real consumer first.

**Why each rating:**
- `GetMyTasks`'s `IsUrgent` **is** the canonical formula already — this migration is close to a no-op: the value doesn't change, only where it's computed and whether `AttentionReason` is now exposed alongside it.
- `MentorController` has a **confirmed internal self-contradiction** today (a task flagged overdue on its own row while excluded from the page's own aggregate count) — fixing this is an intentional, previously-flagged behavior change. Contained to one controller, but the numbers mentors see will move.
- `UpcomingSubmissionsCard` swaps two local-time formulas for the UTC-computed `IsOverdue` flag — a real behavior change for any user outside UTC, but it's a single boolean swap in one card, not a restructuring.
- `ActionCenterCard` is rated 🔴 because migrating it doesn't just change *values*, it changes *which three items are shown and in what order* — the new `AttentionRank` includes two reasons (`PendingMentorReview`, `DueSoon`) that don't exist in the card's current priority scheme at all. The capped top-3 list can change membership, not just relabel. This is the primary "what do I do next" widget on the highest-traffic student screen.
- `StudentTasksPage` + `TaskStatusHelpers` is rated 🔴 for the same reason at larger scale: three tiers (Action Required / Waiting on Others / Upcoming) are all currently derived from the same client-side logic being replaced, so a task can visibly move between tiers after migration. It's also where three separately-maintained `ResolveDisplayStatus` copies finally collapse into one — correct, but the highest-surface-area single change in the whole epic. Migrate last, with full-page before/after screenshots for a few real student accounts.

---

## Recommended global migration order

Interleaving all three services by risk (not by service), so the app accumulates confidence before touching the screens people look at every day:

| Order | Consumer | Service | Class | Screen | Rationale |
|---|---|---|---|---|---|
| 1 | `RoadmapStagesController` | Roadmap | 🟢 | `/project-stages`, `/mentor/roadmap` | Already-trusted code, verbatim move. Proves the extraction pattern end to end. |
| 2 | `ProjectHealthController` | Health | 🟢 | `/project-health` | Same — verbatim move of the canonical formula. |
| 3 | `ProjectsController.GetMyTasks` | Urgency | 🟢 | `/tasks` | Value doesn't change; unblocks `MissingMandatorySubmissionCount` for Health. |
| 4 | `MentorRoadmapOverviewPage` | Roadmap | 🟢 | `/mentor/roadmap` | Deletes a client calc, reads the now-identical server value. |
| 5 | `ProjectOverviewController` (relabel) | Roadmap | 🟢 | `/projects/{id}/overview` | Text-only change. |
| 6 | `ManagementController`, `ProjectOverviewController` (health) | Health | 🟡 | `/management/projects`, `/projects/{id}/overview` | Low-traffic staff screens — safest place to validate "raw column → real value" before touching Mentor-facing pages. |
| 7 | `MilestonesOverviewController` | Health + Roadmap | 🟡 | `/milestones-overview` | Taxonomy change; needs a label-mapping decision, still staff/mentor internal tooling. |
| 8 | `MentorController` | Health + Roadmap + Urgency | 🟡 | `/mentor/projects`, `/mentor/projects/{id}` | Fixes the confirmed self-contradiction; touches three services at once, so give it its own PR per service rather than one combined change. |
| 9 | `UpcomingSubmissionsCard` | Urgency | 🟡 | `/dashboard` | First student-Dashboard-facing change; contained to one boolean. |
| 10 | `LecturerDashboardController` | Health | 🔴 | `/dashboard/mentor`, `/dashboard/lecturer` | Known-disagreement case; highest-trust mentor/staff screen. Ship with an explicit list of projects whose label changes and why. |
| 11 | `ActionCenterCard` | Urgency | 🔴 | `/dashboard` | Ranking/membership change on the primary student widget. |
| 12 | `StudentDashboardHero` | Roadmap | 🔴 | `/dashboard` | Site of the original 62%-vs-52% bug; most-viewed screen in the product. |
| 13 | `StudentTasksPage` + `TaskStatusHelpers` | Urgency | 🔴 | `/tasks` | Largest single change (3-tier restructuring, deletes 3 duplicate helpers); do it last, with the most test coverage already proven by everything above it. |

**Expected impact summary:**
- Steps 1–5 (🟢): no user-visible change expected at all — pure refactor, verifiable by diffing old vs. new output on the existing demo dataset.
- Steps 6–9 (🟡): a handful of numbers/badges will visibly change for specific projects (mostly *newly populated* rather than *wrong before*), confined to staff/mentor tooling and one Dashboard card — low blast radius, each independently revertible.
- Steps 10–13 (🔴): the four screens every role opens most often will show different numbers or different groupings than they do today, in every case *because the old number was already wrong* (the disagreeing lecturer score, the truncated dashboard percentage, the incomplete action-center ranking, the tier-misclassified task). These four should each ship as their own PR with a before/after comparison attached, not bundled together, so a regression in one doesn't block or get lost among the others.

Waiting for review before starting Phase 2 implementation.
