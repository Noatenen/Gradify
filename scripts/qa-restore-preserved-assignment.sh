#!/usr/bin/env bash
# scripts/qa-restore-preserved-assignment.sh
#
# Restores the PROJECT ASSIGNMENT of the preserved QA student from a backup.
#
# ── Why this exists ─────────────────────────────────────────────────────
#
# qa-seed-normalize.sh used to clear assignment state for EVERY team,
# including the preserved QA student's. The account kept its Users row and
# kept its team, so db-check-preserved.sh passed — but Projects.TeamId went
# NULL, StudentStageService correctly resolved NeedsPreferences, and a
# long-lived QA account that has held project 1 since April was dropped back
# into the project catalogue as though it were a brand-new team.
#
# That is now prevented at the source: qa-seed-normalize.sh exempts the
# preserved team. This script repairs a database where it already happened.
#
# ── It never invents an association ─────────────────────────────────────
#
# Every row written here is COPIED from a backup that still holds the state,
# ids included. The script reads which project the preserved student's team
# held in that backup; it does not take a project id as an argument and it
# does not pick one. If the backup does not show that team holding a project,
# it refuses rather than choosing.
#
# Idempotent: re-running against an already-restored database is a no-op.
#
# Usage:  scripts/qa-restore-preserved-assignment.sh [backup.bak] [target.db]
#
# Default backup: the NEWEST file in .local/db-backups/ in which the preserved
# student's active team holds a project.

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

q()  { sqlite3 -noheader "$DB" "$1"; }
qb() { sqlite3 -noheader "$1" "$2"; }

# ── Which team are we restoring? ────────────────────────────────────────
SID=$(q "SELECT Id FROM Users WHERE Email='$STUDENT_EMAIL';")
[ -n "$SID" ] || { echo "ERROR: $STUDENT_EMAIL not in $DB — restore the ACCOUNT first." >&2; exit 1; }

TEAM=$(q "SELECT TeamId FROM TeamMembers WHERE UserId=$SID AND IsActive=1 LIMIT 1;")
[ -n "$TEAM" ] || { echo "ERROR: $STUDENT_EMAIL has no active team — run qa-seed-normalize.sh first." >&2; exit 1; }

echo "Preserved student: $STUDENT_EMAIL (id=$SID), active team $TEAM"

# ── Already correct? ────────────────────────────────────────────────────
HELD=$(q "SELECT Id FROM Projects WHERE TeamId=$TEAM;")
if [ -n "$HELD" ]; then
    echo "Team $TEAM already holds project $HELD — nothing to restore."
    exit 0
fi

# ── Pick the source backup ──────────────────────────────────────────────
pick_backup() {
    local newest="" f pid
    for f in "$BACKUP_DIR"/*.bak; do
        [ -f "$f" ] || continue
        pid=$(qb "$f" "SELECT p.Id FROM Projects p
                       JOIN TeamMembers m ON m.TeamId = p.TeamId
                       JOIN Users u       ON u.Id     = m.UserId
                       WHERE m.IsActive = 1 AND u.Id = $SID LIMIT 1;" 2>/dev/null || true)
        [ -n "$pid" ] && newest="$f"
    done
    printf '%s' "$newest"
}

SRC="${1:-$(pick_backup)}"
[ -n "$SRC" ] && [ -f "$SRC" ] || {
    echo "ERROR: no backup in $BACKUP_DIR shows $STUDENT_EMAIL's team holding a project." >&2
    echo "       Refusing to choose one — restore a backup that has the state." >&2
    exit 1
}

# ATTACH takes a STRING LITERAL, so a path containing a single quote would end
# it early and the remainder would be parsed as SQL. No legitimate backup path
# has one; refuse rather than escape, so there is one less thing to get wrong.
case "$SRC" in
    *"'"*) echo "ERROR: refusing a backup path containing a single quote: $SRC" >&2; exit 1 ;;
esac

# The team id the student sat in AT THAT TIME, which is what the backup's
# Projects.TeamId points at. Normally the same id; read rather than assumed.
SRC_TEAM=$(qb "$SRC" "SELECT TeamId FROM TeamMembers WHERE UserId=$SID AND IsActive=1 LIMIT 1;")
[ -n "$SRC_TEAM" ] || {
    echo "ERROR: $STUDENT_EMAIL has no active team in ${SRC##*/} — that backup cannot" >&2
    echo "       tell us which project the team held. Pick one that can." >&2
    exit 1
}

SRC_PROJ=$(qb "$SRC" "SELECT Id FROM Projects WHERE TeamId=$SRC_TEAM;")
# The load-bearing refusal. A backup that does not show the team holding a
# project is not a source of truth for this, and the alternative — picking a
# free project — is exactly the invention this script exists to avoid. Reached
# when a backup is named explicitly; pick_backup already filters for it.
[ -n "$SRC_PROJ" ] || {
    echo "ERROR: ${SRC##*/} does not show team $SRC_TEAM holding any project." >&2
    echo "       Refusing to invent an assignment. Use a backup that has one." >&2
    exit 1
}
# Exactly one, or we cannot say which was intended.
if [ "$(printf '%s\n' "$SRC_PROJ" | wc -l | tr -d ' ')" != "1" ]; then
    echo "ERROR: ${SRC##*/} shows team $SRC_TEAM holding more than one project:" >&2
    echo "$SRC_PROJ" >&2
    echo "       Refusing to guess which was intended." >&2
    exit 1
fi

SRC_STATUS=$(qb "$SRC" "SELECT Status FROM Projects WHERE Id=$SRC_PROJ;")

echo "Source backup:     ${SRC#$ROOT/}"
echo "Intended state:    team $SRC_TEAM holds project $SRC_PROJ [$SRC_STATUS]"

# ── Refuse to steal a project another team now holds ────────────────────
OWNER=$(q "SELECT IFNULL(TeamId,'') FROM Projects WHERE Id=$SRC_PROJ;")
if [ -n "$OWNER" ] && [ "$OWNER" != "$TEAM" ]; then
    echo "ERROR: project $SRC_PROJ is currently held by team $OWNER." >&2
    echo "       Refusing to reassign it. Resolve that by hand." >&2
    exit 1
fi

# ── Refuse a schema-drifted backup ──────────────────────────────────────
# The copies below are `INSERT ... SELECT *`, which pairs columns BY POSITION.
# If a migration has since added or reordered a column, that silently writes
# values into the wrong fields (or fails on a count mismatch). Compare the
# stored DDL first and refuse on any difference — a drifted backup needs a
# column-by-column restore written by hand, not this script.
drift=""
for t in Projects ProjectTeamProfile TeamTasks StudentSubTasks ProjectResources; do
    a=$(qb "$SRC" "SELECT sql FROM sqlite_master WHERE type='table' AND name='$t';")
    b=$(q            "SELECT sql FROM sqlite_master WHERE type='table' AND name='$t';")
    [ "$a" = "$b" ] || drift="$drift $t"
done
if [ -n "$drift" ]; then
    echo "ERROR: schema differs between ${SRC##*/} and the target for:$drift" >&2
    echo "       Refusing to copy rows positionally across a schema change." >&2
    exit 1
fi
echo "Schema match:      Projects, ProjectTeamProfile, TeamTasks, StudentSubTasks, ProjectResources"

# ── Safety copy ─────────────────────────────────────────────────────────
mkdir -p "$BACKUP_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
cp "$DB" "$BACKUP_DIR/FinalProjectDB.db.$STAMP.pre-assignment-restore.bak"
echo "Backup:            .local/db-backups/FinalProjectDB.db.$STAMP.pre-assignment-restore.bak"

# ── Restore ─────────────────────────────────────────────────────────────
# INSERT OR IGNORE on the team-owned tables, so a partially-restored database
# gains only what it is missing and re-running changes nothing.
sqlite3 "$DB" <<SQL
PRAGMA foreign_keys = ON;
ATTACH DATABASE '$SRC' AS src;
BEGIN;

UPDATE Projects
   SET TeamId = $TEAM,
       Status = (SELECT Status FROM src.Projects WHERE Id = $SRC_PROJ)
 WHERE Id = $SRC_PROJ;

INSERT OR IGNORE INTO ProjectTeamProfile
SELECT * FROM src.ProjectTeamProfile WHERE ProjectId = $SRC_PROJ;

INSERT OR IGNORE INTO TeamTasks
SELECT * FROM src.TeamTasks WHERE TeamId = $SRC_TEAM;

INSERT OR IGNORE INTO StudentSubTasks
SELECT * FROM src.StudentSubTasks WHERE TeamId = $SRC_TEAM;

INSERT OR IGNORE INTO ProjectResources
SELECT * FROM src.ProjectResources WHERE TeamId = $SRC_TEAM;

COMMIT;
DETACH DATABASE src;
SQL

# ── Report ──────────────────────────────────────────────────────────────
echo ""
echo "Restored:"
echo "  project            $(q "SELECT Id||' \"'||Title||'\" ['||Status||'] -> team '||TeamId FROM Projects WHERE Id=$SRC_PROJ;")"
echo "  team profile       $(q "SELECT COUNT(*) FROM ProjectTeamProfile WHERE ProjectId=$SRC_PROJ;") row(s)"
echo "  team tasks         $(q "SELECT COUNT(*) FROM TeamTasks WHERE TeamId=$TEAM;")"
echo "  student sub-tasks  $(q "SELECT COUNT(*) FROM StudentSubTasks WHERE TeamId=$TEAM;")"
echo "  project resources  $(q "SELECT COUNT(*) FROM ProjectResources WHERE TeamId=$TEAM;")"
echo ""

fk=$(q "PRAGMA foreign_key_check;")
if [ -n "$fk" ]; then
    echo "FAIL: foreign_key_check reported violations:" >&2
    echo "$fk" >&2
    exit 1
fi
echo "foreign_key_check: clean"
echo ""

exec "$SCRIPT_DIR/db-check-preserved.sh" "$DB"
