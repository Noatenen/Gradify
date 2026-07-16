# Motiva Demo Data — Technical Specification

Companion to `design/demo-story.md` (read that first for the narrative — this is the field-by-field contract the seeder implements). Development-only. Never runs in Production (see safety section in the seeder itself).

**Enrichment pass (iteration 2):** extended from 3 to 5 projects and 8 to 13 users, specifically to close two cross-screen coherence gaps found by walking Dashboard → Tasks → Project Stages → Submissions → Requests as one continuous story: (1) project 9001 had zero submissions sitting in "Pending" — every mentor-review state except the one actually awaiting review — so the Mentor's Submissions queue looked empty despite an "ongoing" project; (2) project 9002 had milestones but zero Tasks at all, so its My-Tasks-equivalent view was blank. Both are fixed below. Also added a second mentor and two more projects so Staff/lecturer screens show all three `ProjectHealthController` tiers (Green/Orange/Red) instead of just two.

All dates are **relative to seed time** (`Day 0` = `DateTime.Now` when `DemoDataSeeder` runs), stored as absolute timestamps computed at that moment — not re-derived on every read. Re-seeding recomputes them fresh.

## Idempotency contract

- **Master marker**: `Projects.ProjectNumber IN (9001, 9002, 9003, 9004, 9005)`. If `9001` already exists, `SeedAsync` is a complete no-op — it does not patch, merge, or upsert anything.
- **Seed** = create-if-missing only.
- **Reset** = resolve the five `ProjectNumber`s and the 13 fixed demo emails → delete everything belonging to them, in explicit reverse-dependency order (not relying solely on `ON DELETE CASCADE`) → does not touch any other row in the database.
- **Verify** = read-only report: which of the five projects/13 users exist, counts of their child rows (for computed "current stage"/health tier, hit the app's own real endpoints — see the note in the Verify response).

## Users (password for all: `MotivaDemo2026!`, real `PasswordHasher<MinimalUser>` hash, `IsVerified = true`)

| Email | Name | Role | Project |
|---|---|---|---|
| `noa.demo@motiva.local` | נועה כהן | Student | 9001 |
| `ofir.demo@motiva.local` | אופיר שרעבי | Student | 9001 |
| `yanai.mentor.demo@motiva.local` | ינאי כרמי | Mentor | 9001, 9002, 9003, 9005 |
| `jenny.staff.demo@motiva.local` | ג'ני אלון | Staff (global, no project scoping) | — |
| `daniel.demo@motiva.local` | דניאל אבני | Student | 9002 |
| `maya.demo@motiva.local` | מאיה גולן | Student | 9002 |
| `idan.demo@motiva.local` | עידן ברק | Student | 9003 |
| `shira.demo@motiva.local` | שירה מזרחי | Student | 9003 |
| `merav.mentor.demo@motiva.local` | מירב שגיא | Mentor | 9004 |
| `roni.demo@motiva.local` | רוני אשכנזי | Student | 9004 |
| `tal.demo@motiva.local` | טל ברקוביץ' | Student | 9004 |
| `yuval.demo@motiva.local` | יובל שני | Student | 9005 |
| `avigail.demo@motiva.local` | אביגיל נוי | Student | 9005 |

Two mentors, not one, so Staff-facing screens show a real multi-mentor roster rather than one person carrying every team.

## Projects — three `ProjectHealthController` tiers represented

`ProjectHealthController` buckets by the single worst milestone delay: 0 days = Green, ≤14 days = Orange, >14 days = Red.

| ProjectNumber | Title | Type | Team | Mentor | Health |
|---|---|---|---|---|---|
| 9001 | Motiva — Final Project Management Platform | Technological | Noa + Ofir | Yanai | Red (16d — the returned UX Spec milestone) |
| 9002 | מערכת ניהול ספרייה דיגיטלית | Technological | Daniel + Maya | Yanai | **Green** |
| 9003 | פלטפורמת מסחר אלקטרוני לעסקים קטנים | Technological | Idan + Shira | Yanai | Red (25d) |
| 9004 | אפליקציית מעקב בריאות דיגיטלית לקשישים | Methodological | Roni + Tal | Merav | **Orange** (7d) |
| 9005 | אפליקציית אימונים אישית מבוססת AI | Technological | Yuval + Avigail | Yanai | **Green**, further along (current stage: Evaluation) |

9001 landing on Red isn't a separate design choice — it falls out honestly from the same returned-milestone story already documented above; the point of 9002/9005 vs. 9004 vs. 9001/9003 is that all three tiers are now real, not that 9001 is artificially "healthy."

Academic year: reuses whatever cycle currently has `IsCurrent = 1`. If none exists (fresh DB), the seeder creates one dedicated cycle and marks it current.

**A note on a real bug found and fixed during this enrichment**: every "completed on time" milestone across the whole file originally had its `CompletedAt` offset written *less negative* than its `DueDate` offset (e.g. due `Day(-60)`, completed `Day(-58)`) — which is chronologically *after* the deadline, not before it, since less-negative means closer to "now." This silently pushed 9002 and 9005 into Orange instead of the intended Green. Fixed by scripting every occurrence to complete exactly one day before its due date, then re-verified against the real `/api/project-health` endpoint until the tiers matched intent.

## Project 9001 — the primary story

### Roadmap stages (existing seeded rows, reused as-is)

| Order | Code | Name | Linked milestones | Derived status |
|---|---|---|---|---|
| 1 | Selection | בחירה ושיבוצים | 1, all completed | Completed |
| 2 | Kickoff | התנעה | 1, all completed | Completed |
| 3 | Definition | הגדרת הבעיה... | 1, all completed | Completed |
| 4 | Specification | אפיון | **8**, 5 completed | **Current — 62%** |
| 5 | Development | פיתוח | 1, not started | Future |
| 6 | Evaluation | הערכה | 1, not started | Future |
| 7 | SubmissionGrading | הגשות וציונים | 1, not started | Future |

62% is exact: `round(100 × 5 / 8) = 62` (`Math.Round`, default banker's rounding — 62.5 → 62). This is why the stage has 8 deliverables, not a round number.

### Specification stage — the 8 deliverables

| # | Milestone (new `MilestoneTemplate`) | Status | Notes |
|---|---|---|---|
| 1 | תוכנית מחקר UX | Completed | Due Day−49, completed Day−48 |
| 2 | סיכום ראיונות משתמשים | Completed | Due Day−35, completed Day−34 |
| 3 | הגדרת פרסונות | Completed | Due Day−28, completed Day−27 |
| 4 | מיפוי תהליכי משתמש | Completed | Due Day−25, completed Day−24 |
| 5 | Wireframes ברמת דיוק נמוכה | Completed | Due Day−21, completed Day−20 |
| 6 | **מסמך אפיון UX** | InProgress | Submission — returned by mentor, see below |
| 7 | **Wireframes ברמת דיוק גבוהה** | InProgress | Submission — due **Day+2**, not yet submitted |
| 8 | תוכנית בדיקות שימושיות | NotStarted | Due Day+10 |

Stages 1–3 and 5–7 each get one simple milestone (Completed for 1–3, NotStarted for 5–7) purely to make the "current stage" derivation land correctly — not part of the narrative focus.

### Tasks (10)

| Title | Assignee | Status | Due | Milestone |
|---|---|---|---|---|
| כתיבת תוכנית מחקר UX | Noa | Done | Day−49 | #1 — *IsSubmission* |
| ניתוח ראיונות והגדרת פרסונות | Noa | Done | Day−28 | #3 |
| מיפוי תהליכי משתמש עיקריים | Ofir | Done | Day−25 | #4 |
| עיצוב Wireframes ברמת דיוק נמוכה | Ofir | Done | Day−21 | #5 |
| **כתיבת מסמך אפיון UX** | Noa | ReturnedForRevision | Day−14 | #6 — *IsSubmission* |
| **בניית Wireframes ברמת דיוק גבוהה** | Ofir | InProgress | Day+2 | #7 — *IsSubmission* |
| **שיתוף גרסת ביניים של Wireframes לבדיקת המנחה** | Ofir | SubmittedToMentor | Day−1 | #7 — *IsSubmission, genuinely Pending review* |
| תכנון תוכנית בדיקות שימושיות | *whole team* (null) | Open | Day+10 | #8 |
| **עדכון סיכום ראיונות משתמשים לפי הערות המנחה** | Ofir | Open — **overdue** | Day−4 | #2 (follow-up) |
| תיאום ישיבת סטטוס שבועית עם המנחה | *whole team* (null) | InProgress | Day+5 | — (general) |

### Submissions (3) — one of each `MentorStatus`, a real history not one incident

| Task | Submitted | MentorStatus | Notes |
|---|---|---|---|
| כתיבת תוכנית מחקר UX | Day−49 | **Approved** | Reviewed Day−47, "עבודה טובה, אפשר להמשיך הלאה." |
| כתיבת מסמך אפיון UX | Day−14 | **Returned** | Reviewed Day−3, four itemized comments (below) |
| שיתוף גרסת ביניים של Wireframes | Day−1 | **Pending** | Not yet reviewed — the one genuinely live item in Yanai's queue |

`MentorFeedback` on the returned submission (single free-text field — **no per-comment table exists**, so the four comments are one itemized block, not four rows):
```
1. הפרסונה המרכזית סותרת יעד שהוגדר קודם — יש ליישב את הסתירה.
2. תהליך ה-Onboarding חסר מסלול למקרה של שגיאה.
3. לא מטופלות דרישות נגישות עבור מסכי המנחה.
4. פרק מדדי ההצלחה לא מקושר בחזרה להגדרת הבעיה המקורית.
```

All `DriveUrl` values are generated as opaque, random-looking 33-character IDs (`RandomDriveUrl()`) — a descriptive slug is actually *less* realistic than Drive's real, unreadable share-link format.

### Request (1, pending)

- `ProjectRequests`: `RequestType = Extension`, `CreatedByUserId = Noa`, `Status = PendingMentorRecommendation`, `Priority = Normal`, `CreatedAt = Day−2`, title "בקשת דחייה להגשת Wireframes ברמת דיוק גבוהה".
- `ProjectRequestExtensions`: `TaskId` = the Wireframes-hi-fi task, `CurrentDueDate = Day+2`, `RequestedDueDate = Day+5`, `MentorDecision = Pending`, `LecturerDecision = NotRequired`, `FinalDecision = Pending`.
- One `ProjectRequestEvents` row (`EventType = Comment`, by Yanai, Day−1: "ראיתי, אבדוק ואחזור אליכם בקרוב") — gives the request thread a non-zero comment count.

### Notifications (6, mixed read state)

| Type (canonical `NotificationTypes`) | To | Created | Read? |
|---|---|---|---|
| `SubmissionFeedbackReceived` | Noa | Day−3 | Unread |
| `TaskOverdue` | Ofir | Day−1 | Unread |
| `RequestStatusChanged` | Noa | Day−2 | Unread |
| `RequestStatusChanged` | Ofir | Day−2 | **Read** |
| `General` *(announcement stand-in — see honesty note in demo-story.md)* | Noa + Ofir | Day−1 | Unread |
| `TaskDueSoon` | Ofir | Day−1 | Unread |
| `SubmissionSubmitted` | **Yanai** | Day−1 | Unread — mentor-side echo of the new pending submission |

### Request (1, pending)

- `ProjectRequests`: `RequestType = Extension`, `CreatedByUserId = Noa`, `Status = PendingMentorRecommendation`, `Priority = Normal`, `CreatedAt = Day−2`, title "בקשת דחייה להגשת Wireframes ברמת דיוק גבוהה".
- `ProjectRequestExtensions`: `TaskId` = the Wireframes-hi-fi task, `CurrentDueDate = Day+2`, `RequestedDueDate = Day+5`, `MentorDecision = Pending`, `LecturerDecision = NotRequired`, `FinalDecision = Pending`.
- One `ProjectRequestEvents` row (`EventType = Comment`, by Yanai, Day−1: "ראיתי, אבדוק ואחזור אליכם בקרוב") — gives the request thread a non-zero comment count.

## Project 9002 — "on track" (Green), now with real task/submission history

- Selection, Kickoff, Definition, Specification: 1 completed milestone each, all completed the day before their due date (0 delay).
- Specification's milestone ("מסמך אפיון ראשוני") now has a real task + an **Approved** submission (Daniel, reviewed on time, "מסמך מסודר וברור, מאושר להמשך.") — closes the gap where this project previously had milestones but zero tasks.
- Two more tasks: UI-library selection (Done, Maya) and DB schema planning (InProgress, Daniel, due Day+8).
- One **Resolved** `Meeting`-type request (Maya) — shows a closed/historical request, not just pending ones, for variety on the Staff Requests screen.

## Project 9003 — "at risk" (Red)

- Selection, Kickoff: 1 completed milestone each, on time.
- Definition (current): milestone "מסמך הגדרת בעיה" still open, due Day−25 → **25 days delayed → Red**.
- One submission task under that milestone, returned by the mentor (`MentorStatus = Returned`, feedback: "הניתוח לא מכסה את קהל היעד המרכזי, יש לעדכן").
- One additional plain overdue task (Idan, due Day−10, still Open).
- One **InProgress** `TechnicalSupport` request (Shira, High priority) — a payment-gateway API integration problem, with a staff/mentor comment already on the thread — shows an actively-worked, non-Extension request type.

## Project 9004 — "needs attention" (Orange, 7-day delay), different mentor and track

- Team: Roni + Tal, mentored by **Merav** (not Yanai) — Methodological track, not Technological.
- Selection, Kickoff, Definition: 1 completed milestone each, on time.
- Development (current): milestone "אב-טיפוס עובד ראשוני" open, due Day−7 → **Orange**. Its task has a **Pending** submission (Roni, submitted Day−2) — genuinely awaiting Merav's review.
- One more task: accessibility testing for elderly users (Tal, Open, due Day+4).
- One Extension request at **`PendingLecturerDecision`** — the *other* stage of the two-step extension flow (9001's request is still at `PendingMentorRecommendation`), with Merav's own recommendation already logged on the thread ("ממליצה לאשר, העיכוב מוצדק טכנית").

## Project 9005 — "healthy and further along" (Green), Evaluation stage

- Team: Yuval + Avigail, mentored by Yanai — demonstrates a project much further down the roadmap than the others (current stage: **Evaluation**, stage 6 of 7), not stuck early.
- Selection, Kickoff, Definition, Specification, Development: 1 completed milestone each, all on time.
- Evaluation (current): milestone "תוכנית הערכת משתמשים" open, due Day+10 (not yet overdue) → **Green**. Its task is already Done with an **Approved** submission (Yuval, "תוכנית מקיפה, מוכנים להתחיל בבדיקות.").
- One more task: recruiting usability-test participants (Avigail, InProgress, due Day+7).
- One **Resolved** `SpecialEvent` request (Avigail) — asking to present at the course's end-of-year event, approved by Jenny (Staff) directly.

## A bug worth documenting: submission dates were checked too

While enriching, every `submittedAt` → `mentorReviewedAt` pair was also audited the same way (reviewed must come chronologically after submitted) — all were already correct. Only the "completed before due date" pattern (above) had the sign confusion, and only in `SeedSimpleCompletedMilestoneAsync` / `CreateFullMilestoneAsync` calls specifically.

## Expected appearance on each main screen (login as `noa.demo@motiva.local` unless noted)

- **Dashboard** — Today's Focus surfaces the returned UX Specification Document (top priority tier per the shared rule). My Tasks lists the rest, ranked: the overdue interview-summary update next, then the due-in-2-days Wireframes submission, then the team status-meeting task. Sidebar shows "אפיון" / 5 מתוך 7 / 62%.
- **My Tasks** — all 10 tasks visible across the 4-tier hierarchy; the returned submission, the pending-review check-in, and the overdue item all surface in the top tiers.
- **Project Stages** — Selection/Kickoff/Definition shown completed; Specification expanded as current with all 8 deliverables listed at 62%; Development/Evaluation/SubmissionGrading shown as compact future stages.
- **Submissions / mentor review** (login as `yanai.mentor.demo@motiva.local`) — the queue now genuinely has something live: the Wireframes check-in sitting at Pending, alongside the already-resolved Approved and Returned history for 9001, plus 9004's Pending item (as Merav) and 9002/9005's Approved history.
- **Requests** (as Noa or Ofir) — one pending Extension request, status "ממתינה להמלצת מנחה," one comment from Yanai visible in the thread. Staff view additionally sees 9002's Resolved Meeting, 9003's InProgress TechnicalSupport, 9004's PendingLecturerDecision Extension, and 9005's Resolved SpecialEvent — five requests spanning four types and four statuses.
- **Notifications / header bell** — 6 items for Noa/Ofir, mixed read state; Yanai also has one (the new pending submission) — the mentor's own bell reflects the same event the students see.
- **Mentor view** (`yanai.mentor.demo@motiva.local`) — sees 9001, 9002, 9003, 9005 (not 9004, mentored by Merav).
- **Staff / Course-staff view** (`jenny.staff.demo@motiva.local`) — sees all five projects globally; Project Health list shows all three tiers represented (9001/9003 Red, 9004 Orange, 9002/9005 Green), sorted most-concerning-first, with two different mentors' names visible across the roster — verified directly against the real `/api/project-health` screenshot, not just the API response.
