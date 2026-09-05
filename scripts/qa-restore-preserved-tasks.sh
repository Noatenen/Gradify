#!/usr/bin/env bash
# scripts/qa-restore-preserved-tasks.sh
#
# Restores the preserved QA student's PROJECT WORK — milestones, tasks,
# submissions — from a backup that still holds it.
#
# ── Why this exists ─────────────────────────────────────────────────────
#
# The 03.09/04.09 reset work rebuilt the preserved student's account, team and
# project assignment, and scripts/qa-restore-preserved-assignment.sh repairs
# that last relationship. None of it covers what hung OFF the project: the
# ProjectMilestones rows, the Tasks bound to them, and the TaskSubmissions
# bound to those. Project 1 came out of the reset with 5 TeamTasks and nothing
# else, so every student surface that reads real project work — the dashboard's
# attention card and upcoming deadlines, the Tasks page's filters, submitted vs
# pending state — had no data to show and could not be verified.
#
# The rows are still in .local/db-backups/FinalProjectDB.db.20260904-115506.bak,
# the last backup taken before that reset. This script copies them back.
#
# ── It never invents a row ──────────────────────────────────────────────
#
# Every row written here is COPIED from the backup with its own id, its own
# foreign keys and its own state. Nothing is re-dated, no status is
# "refreshed", and no row is synthesised to make a screen look better. If the
# chosen backup does not hold project work for the preserved team, the script
# refuses rather than picking a different project's data.
#
# TEAM TASKS ARE NOT TOUCHED. TeamTasks/StudentSubTasks survived the reset and
# are the team's live data; this script only fills what was lost. StudentSubTasks
# is the one table it writes that the team also owns, and it INSERTs only ids
# that are absent.
#
# Idempotent: every statement is INSERT OR IGNORE keyed on the primary key, so
# re-running against an already-restored database writes nothing. It is safe to
# run before or after qa-restore-preserved-assignment.sh.
#
# ── THIS IS A LOCAL REPAIR TOOL, NOT A SEED ─────────────────────────────
#
# It restores one developer's QA fixture from THAT developer's own local
# backups. It is NOT a fresh-clone bootstrap and must never be treated as one:
#
#   * `.local/db-backups/` is deliberately untracked and stays local. Nothing
#     in this repository ships the rows — they exist only in a backup taken
#     before the reset that lost them.
#   * On a machine with no such backup the script REFUSES and writes nothing
#     (verified: exit 1, "no backup … holds tasks for project N"). It will not
#     fall back to another project's data, and it will not invent rows to make
#     a screen look populated.
#   * A fresh clone therefore has no QA fixture and is expected not to. Getting
#     one means obtaining a backup that holds it, not running this script.
#
# ── THE UPCOMING FIXTURE IS DATE-SENSITIVE ──────────────────────────────
#
# The restored dataset's newest due date is 2026-05-26, so on any date after
# ~June 2026 it provides no "upcoming" coverage at all. The single row this
# script adds is pinned to AcademicYearMilestones 146, due **2026-09-15**.
#
# AFTER 2026-09-15 THAT ROW IS IN THE PAST AND UPCOMING COVERAGE IS GONE.
# db-check-preserved.sh warns when nothing is still ahead of today; that
# warning is the signal to re-point FUTURE_AYM below at whatever milestone is
# then ahead, or to re-seed the fixture entirely.
#
# It is pinned ON PURPOSE. Deriving the date from `date('now')` would keep the
# fixture permanently "upcoming" and would make it a moving target: the row's
# due date would differ on every machine and after every run, so a dashboard
# screenshot could never be compared with another, and a bug that only appears
# near a deadline boundary could never be reproduced. A fixture that expires
# loudly is worth more than one that silently drifts.
#
# Usage:  scripts/qa-restore-preserved-tasks.sh [backup.bak] [target.db]

set -euo pipefail

SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT="$( cd -- "$SCRIPT_DIR/.." &> /dev/null && pwd )"

STUDENT_EMAIL="noa.qa@motiva.local"
BACKUP_DIR="$ROOT/.local/db-backups"
DB="${2:-$ROOT/Server/FinalProjectDB.db}"

case "$DB" in
    *FinalProjectDB.db) ;;
    *) echo "ERROR: refusing to write '$DB' — not a FinalProjectDB.db." >&2; exit 1 ;;
esac
[ -f "$DB" ] || { echo "ERROR: database not found at $DB" >&2; exit 1; }

# ── Resolve the preserved student → team → project in the TARGET ────────
SID=$(sqlite3 "$DB" "SELECT Id FROM users WHERE Email='$STUDENT_EMAIL' LIMIT 1;")
[ -n "$SID" ] || { echo "ERROR: $STUDENT_EMAIL not in $DB — restore the ACCOUNT first." >&2; exit 1; }

TEAM=$(sqlite3 "$DB" "SELECT TeamId FROM TeamMembers WHERE UserId=$SID AND IsActive=1 LIMIT 1;")
[ -n "$TEAM" ] || { echo "ERROR: $STUDENT_EMAIL has no active team — run qa-seed-normalize.sh first." >&2; exit 1; }

PROJ=$(sqlite3 "$DB" "SELECT Id FROM Projects WHERE TeamId=$TEAM LIMIT 1;")
[ -n "$PROJ" ] || {
    echo "ERROR: team $TEAM holds no project — run qa-restore-preserved-assignment.sh first." >&2
    exit 1
}

# ── Choose the backup ───────────────────────────────────────────────────
# Explicit argument, else the NEWEST backup that actually holds tasks for this
# project. "Holds the state" is the test, never the filename or the date.
pick_backup() {
    local f
    for f in $(ls -1t "$BACKUP_DIR"/*.bak 2>/dev/null); do
        local n
        n=$(sqlite3 "$f" "SELECT COUNT(*) FROM Tasks WHERE ProjectId=$PROJ;" 2>/dev/null || echo 0)
        [ "${n:-0}" -gt 0 ] && { echo "$f"; return 0; }
    done
    return 1
}

BAK="${1:-}"
if [ -z "$BAK" ]; then
    BAK=$(pick_backup) || {
        echo "ERROR: no backup in $BACKUP_DIR holds tasks for project $PROJ." >&2
        echo "       Refusing to choose another project's data." >&2
        exit 1
    }
fi
[ -f "$BAK" ] || { echo "ERROR: backup not found: $BAK" >&2; exit 1; }

SRC_TASKS=$(sqlite3 "$BAK" "SELECT COUNT(*) FROM Tasks WHERE ProjectId=$PROJ;")
[ "${SRC_TASKS:-0}" -gt 0 ] || {
    echo "ERROR: $(basename "$BAK") holds no tasks for project $PROJ." >&2
    exit 1
}

echo "Preserved student : $STUDENT_EMAIL (id $SID)"
echo "Team / project    : $TEAM / $PROJ"
echo "Backup            : $(basename "$BAK")  ($SRC_TASKS tasks)"

# ── Safety copy ─────────────────────────────────────────────────────────
mkdir -p "$BACKUP_DIR"
SAFETY="$BACKUP_DIR/$(basename "$DB").$(date +%Y%m%d-%H%M%S).pre-task-restore.bak"
cp "$DB" "$SAFETY"
echo "Safety copy       : $(basename "$SAFETY")"

# ── Copy the rows ───────────────────────────────────────────────────────
# ATTACH rather than dump/reload: it copies whole rows with their ids in one
# transaction and cannot reorder columns. INSERT OR IGNORE on the primary key
# is what makes every statement idempotent.
#
# Order matters — ProjectMilestones before Tasks (Tasks.ProjectMilestoneId),
# Tasks before TaskSubmissions, TaskSubmissions before TaskSubmissionFiles.
sqlite3 "$DB" <<SQL
PRAGMA foreign_keys = ON;
ATTACH DATABASE '$BAK' AS src;
BEGIN;

INSERT OR IGNORE INTO main.ProjectMilestones
SELECT * FROM src.ProjectMilestones WHERE ProjectId = $PROJ;

INSERT OR IGNORE INTO main.Tasks
SELECT * FROM src.Tasks WHERE ProjectId = $PROJ;

INSERT OR IGNORE INTO main.TaskSubmissions
SELECT s.* FROM src.TaskSubmissions s
JOIN src.Tasks t ON t.Id = s.TaskId
WHERE t.ProjectId = $PROJ;

INSERT OR IGNORE INTO main.TaskSubmissionFiles
SELECT f.* FROM src.TaskSubmissionFiles f
JOIN src.TaskSubmissions s ON s.Id = f.TaskSubmissionId
JOIN src.Tasks t ON t.Id = s.TaskId
WHERE t.ProjectId = $PROJ;

-- Team-owned, and the team's live rows win: OR IGNORE means an id that already
-- exists is left exactly as it is.
INSERT OR IGNORE INTO main.StudentSubTasks
SELECT * FROM src.StudentSubTasks WHERE TeamId = $TEAM;

INSERT OR IGNORE INTO main.ProjectSubmissionStatuses
SELECT * FROM src.ProjectSubmissionStatuses WHERE ProjectId = $PROJ;

COMMIT;
DETACH DATABASE src;
SQL

# ── The one row the backup cannot supply: an UPCOMING submission ────────
#
# The restored dataset was authored around May 2026 and its newest due date is
# 2026-05-26, so after 2026-09-05 every historical row is in the past. Nothing
# in any backup can exercise the dashboard's upcoming-deadlines area or the
# Tasks page's not-yet-due states, and re-dating a restored row would destroy
# the overdue coverage that is the point of the restore.
#
# So exactly ONE row is added, and both halves of it are copied from rows that
# already exist rather than invented:
#   * its SHAPE — milestone-bound, InProgress, future due date — from the live
#     QA row Tasks 621 on project 133, the product's only existing example of
#     an upcoming task;
#   * its SUBMISSION attributes — IsSubmission, RequiresClosure, TaskType and
#     the Drive instructions — from THIS project's own Tasks 548, so the
#     upcoming item behaves like the submissions the team already has.
#
# Its due date is not chosen either: it is AcademicYearMilestones 146
# ("תוכנית הערכת משתמשים", 2026-09-15), a real milestone already in project 1's
# own cycle and the only one still ahead of today.
#
# Keyed on a fixed Title so the INSERT ... WHERE NOT EXISTS is idempotent
# without hard-coding an id that a later seed might take.
UPCOMING_TITLE='תוכנית הערכת משתמשים — הגשה'
FUTURE_AYM=146

sqlite3 "$DB" <<SQL
PRAGMA foreign_keys = ON;
BEGIN;

-- Bind the project to the future milestone, if it is not already.
INSERT INTO ProjectMilestones (ProjectId, AcademicYearMilestoneId, Status)
SELECT $PROJ, $FUTURE_AYM, 'InProgress'
WHERE NOT EXISTS (
    SELECT 1 FROM ProjectMilestones
    WHERE ProjectId = $PROJ AND AcademicYearMilestoneId = $FUTURE_AYM
);

INSERT INTO Tasks
    (ProjectId, ProjectMilestoneId, Title, Description, TaskType, Status, DueDate,
     CreatedByUserId, IsMandatory, IsSystemTask, RequiresClosure,
     CreatedAt, IsSubmission, SubmissionInstructions)
SELECT
    $PROJ,
    (SELECT Id FROM ProjectMilestones
      WHERE ProjectId = $PROJ AND AcademicYearMilestoneId = $FUTURE_AYM LIMIT 1),
    '$UPCOMING_TITLE',
    'הגשת תוכנית הערכת המשתמשים של הפרויקט, לקראת מועד ההגשה בסוף המחזור.',
    'System', 'InProgress',
    (SELECT DueDate FROM AcademicYearMilestones WHERE Id = $FUTURE_AYM),
    (SELECT CreatedByUserId FROM Tasks WHERE ProjectId = $PROJ AND IsSubmission = 1
      ORDER BY Id DESC LIMIT 1),
    0, 0, 1,
    datetime('now'), 1,
    (SELECT SubmissionInstructions FROM Tasks WHERE ProjectId = $PROJ AND IsSubmission = 1
      AND SubmissionInstructions IS NOT NULL ORDER BY Id DESC LIMIT 1)
WHERE NOT EXISTS (
    SELECT 1 FROM Tasks WHERE ProjectId = $PROJ AND Title = '$UPCOMING_TITLE'
);

COMMIT;
SQL

# ── Report ──────────────────────────────────────────────────────────────
echo
echo "Restored (target now holds):"
sqlite3 "$DB" "
SELECT '  ProjectMilestones  : ' || COUNT(*) FROM ProjectMilestones WHERE ProjectId=$PROJ;
SELECT '  Tasks              : ' || COUNT(*) FROM Tasks WHERE ProjectId=$PROJ;
SELECT '  TaskSubmissions    : ' || COUNT(*) FROM TaskSubmissions s JOIN Tasks t ON t.Id=s.TaskId WHERE t.ProjectId=$PROJ;
SELECT '  TaskSubmissionFiles: ' || COUNT(*) FROM TaskSubmissionFiles f JOIN TaskSubmissions s ON s.Id=f.TaskSubmissionId JOIN Tasks t ON t.Id=s.TaskId WHERE t.ProjectId=$PROJ;
SELECT '  StudentSubTasks    : ' || COUNT(*) FROM StudentSubTasks WHERE TeamId=$TEAM;
SELECT '  SubmissionStatuses : ' || COUNT(*) FROM ProjectSubmissionStatuses WHERE ProjectId=$PROJ;
SELECT '  TeamTasks (kept)   : ' || COUNT(*) FROM TeamTasks WHERE TeamId=$TEAM;
"

# Foreign-key integrity is the real proof the copy landed consistently.
FK=$(sqlite3 "$DB" "PRAGMA foreign_key_check;" | head -5)
if [ -n "$FK" ]; then
    echo
    echo "WARNING: foreign_key_check reported rows:" >&2
    echo "$FK" >&2
    exit 1
fi

echo
echo "OK — foreign keys clean."
