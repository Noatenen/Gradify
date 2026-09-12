-- scripts/seed-final-demo-data.sql
--
-- FINAL FACULTY-DEMO DATA SEED — reconnect + light refresh of the EXISTING demo
-- dataset. This does NOT create a second parallel dataset. The four demo
-- projects (9001/9002/9003/9005 = ids 129/130/131/133) already carry their
-- tasks, submissions, requests, milestones and notifications; they were simply
-- never linked to their teams (Projects.TeamId was NULL) so the students saw
-- empty screens. This seed completes that wiring and adds a small "current"
-- refresh.
--
-- IDEMPOTENT: every statement is an UPDATE / guarded INSERT / targeted DELETE,
-- so running it twice produces the same final state and never duplicates.
--
-- Real/QA data (users 1-18, team 1, project 1 "Gradify Platform", project 2)
-- is never touched. No users are deleted. No passwords/auth touched.

PRAGMA foreign_keys = ON;

BEGIN;

-- ── A. Reconnect the 4 active demo teams to their projects (the missing link) ──
UPDATE Projects SET TeamId = 129 WHERE Id = 129 AND TeamId IS NULL;
UPDATE Projects SET TeamId = 130 WHERE Id = 130 AND TeamId IS NULL;
UPDATE Projects SET TeamId = 131 WHERE Id = 131 AND TeamId IS NULL;
UPDATE Projects SET TeamId = 133 WHERE Id = 133 AND TeamId IS NULL;

-- ── B. Mentor distribution (exactly one supervisor per demo team) ──
--   129 Motiva            -> ADMIN ADMIN (12)
--   130 ספרייה דיגיטלית    -> אבי לוי (3)
--   131 מסחר אלקטרוני       -> נטע סורק (7)
--   133 אימונים AI          -> מירב שגיא (69)
DELETE FROM ProjectMentors WHERE ProjectId = 129 AND UserId <> 12;
INSERT INTO ProjectMentors (ProjectId, UserId)
  SELECT 129, 12 WHERE NOT EXISTS (SELECT 1 FROM ProjectMentors WHERE ProjectId = 129 AND UserId = 12);
DELETE FROM ProjectMentors WHERE ProjectId = 130 AND UserId <> 3;
INSERT INTO ProjectMentors (ProjectId, UserId)
  SELECT 130, 3 WHERE NOT EXISTS (SELECT 1 FROM ProjectMentors WHERE ProjectId = 130 AND UserId = 3);
DELETE FROM ProjectMentors WHERE ProjectId = 131 AND UserId <> 7;
INSERT INTO ProjectMentors (ProjectId, UserId)
  SELECT 131, 7 WHERE NOT EXISTS (SELECT 1 FROM ProjectMentors WHERE ProjectId = 131 AND UserId = 7);
DELETE FROM ProjectMentors WHERE ProjectId = 133 AND UserId <> 69;
INSERT INTO ProjectMentors (ProjectId, UserId)
  SELECT 133, 69 WHERE NOT EXISTS (SELECT 1 FROM ProjectMentors WHERE ProjectId = 133 AND UserId = 69);

-- ── B2. אבי לוי (3) supervises EXACTLY ONE project in the final demo ──
--
-- §B above fixes the four demo projects, but it says nothing about the legacy
-- QA projects — 1 "Gradify Platform" and 7 "עמותת בוגרי יהלם" — which אבי לוי
-- picked up through the UI in April and June. They are real rows, not seed
-- artefacts, and they were leaking into every mentor surface: בית, משימות
-- לבדיקה and פרויקטים בהנחייתי all listed work on projects that are not part
-- of the demo story.
--
-- Stated declaratively rather than as "delete 1 and 7", so the rule survives
-- any project added later: in the final demo, אבי לוי mentors 130 and nothing
-- else. Idempotent, and re-running it after §B is a no-op.
--
-- SAFE — this orphans nothing. Every project he is being detached from keeps
-- another mentor: 1 -> admin admin (12), 7 -> ינאי זגורי (4), 129 -> (12),
-- 131 -> נטע סורק (7), 133 -> מירב שגיא (69). Only the ProjectMentors link is
-- touched; no user, team, project, task or submission is deleted, so the
-- "real/QA data is never touched" contract at the top of this file still holds
-- for every row that represents actual work.
DELETE FROM ProjectMentors WHERE UserId = 3 AND ProjectId <> 130;
INSERT INTO ProjectMentors (ProjectId, UserId)
  SELECT 130, 3 WHERE NOT EXISTS (SELECT 1 FROM ProjectMentors WHERE ProjectId = 130 AND UserId = 3);

-- ── C. Membership repair: user 73 (אביגיל נוי) owns a task + a request on
--        project 133 but was not a member of team 133 -> add her (no orphan). ──
INSERT INTO TeamMembers (TeamId, UserId, IsActive, MemberRole)
  SELECT 133, 73, 1, 'Member'
  WHERE NOT EXISTS (SELECT 1 FROM TeamMembers WHERE TeamId = 133 AND UserId = 73);
UPDATE TeamMembers SET IsActive = 1 WHERE TeamId = 133 AND UserId = 73;

-- ── D. Pending-assignment board: keep exactly 3 (teams 134/136/138, the ones
--        with reviewer notes), remove 2 (135/137). The board lists teams that
--        have an AssignmentFormSubmission and no assigned project, so removing
--        the demo form submission (and its preferences) cleanly takes the team
--        off the board. Teams, members and users are all preserved. ──
DELETE FROM TeamProjectPreferences   WHERE TeamId IN (135, 137);
DELETE FROM AssignmentFormSubmissions WHERE TeamId IN (135, 137);

-- ── E. Freshness: ~6 current tasks so the system looks active around the demo
--        date. Dates are relative to now(); statuses use only real Tasks values
--        (Open / InProgress). Idempotent via (ProjectId, Title). Distribution is
--        deliberately uneven (129:2, 130:1, 131:2, 133:1). ──

-- Team 129 (mentor 12) — due soon + upcoming
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 129, 'עדכון תרשים ERD', 'עדכון מודל הנתונים בהתאם למשוב שהתקבל בשלב האפיון', 'ProjectTask', 'Open', datetime('now','+4 days'), 12, 61, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 129 AND Title = 'עדכון תרשים ERD');
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 129, 'הכנת מצגת התקדמות', 'ריכוז ההתקדמות לקראת מפגש המנחה', 'ProjectTask', 'Open', datetime('now','+12 days'), 12, 62, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 129 AND Title = 'הכנת מצגת התקדמות');

-- Team 130 (mentor 3) — in progress
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 130, 'השלמת מסמך אפיון', 'השלמת פרקי מסמך האפיון החסרים לפני ההגשה', 'ProjectTask', 'InProgress', datetime('now','+8 days'), 3, 65, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 130 AND Title = 'השלמת מסמך אפיון');

-- Team 131 (mentor 7) — due soon + recently overdue
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 131, 'בדיקת תרחישי משתמש', 'מעבר על תרחישי המשתמש המרכזיים ואיתור פערים', 'ProjectTask', 'Open', datetime('now','+3 days'), 7, 68, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 131 AND Title = 'בדיקת תרחישי משתמש');
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 131, 'סגירת משוב מנחה', 'סגירת הערות המנחה מהמפגש האחרון', 'ProjectTask', 'InProgress', datetime('now','-3 days'), 7, 67, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 131 AND Title = 'סגירת משוב מנחה');

-- Team 133 (mentor 69) — upcoming
INSERT INTO Tasks (ProjectId, Title, Description, TaskType, Status, DueDate, CreatedByUserId, AssignedToUserId, IsMandatory, IsSystemTask, IsSubmission, CreatedAt)
  SELECT 133, 'הכנת גרסת בדיקות', 'הכנת גרסה להרצת בדיקות קבלה', 'ProjectTask', 'Open', datetime('now','+15 days'), 69, 71, 0, 0, 0, datetime('now')
  WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE ProjectId = 133 AND Title = 'הכנת גרסת בדיקות');

COMMIT;
