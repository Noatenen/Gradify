#!/usr/bin/env bash
# scripts/qa-seed-normalize.sh
#
# Brings the local database to the QA TARGET STATE for testing the assignment
# flow end to end. Idempotent: running it twice produces the same state.
#
# ── THE THREE CONCEPTS, KEPT SEPARATE ───────────────────────────────────
#
# They are not the same thing, and conflating them is what produced both bugs
# we have hit. Stated per preserved account:
#
#   PRESERVE ACCOUNT           the Users row, its credentials and its roles
#   PRESERVE TEAM              an active TeamMembers row, so the account is
#                              never stranded (see db-check-preserved.sh)
#   PRESERVE PROJECT ASSIGNMENT  Projects.TeamId pointing at that team
#
#   noa.qa@motiva.local    account YES   team YES   assignment YES
#   avi.mentor.qa@motiva.local  account YES   team n/a   assignment n/a   (Mentor)
#   admin.qa@motiva.local  account YES   team n/a   assignment n/a   (Admin)
#
# Preserving the account while dropping the team leaves a user trapped between
# login and a /create-team form that refuses their own email.
#
# ── assignment was NO, and that was wrong (Sept 2026) ───────────────────
#
# This script used to clear the preserved student's assignment along with
# everyone else's, on the reasoning that "the QA dataset starts with nothing
# assigned". That reasoning does not survive contact with the account it was
# applied to. noa.qa has held project 1 since April: its dashboard, its
# /project workspace, its team tasks and its resources are the reference
# state for every Student screen. Clearing it does not produce a clean
# fixture, it produces a DIFFERENT user — one who is sent to the catalogue on
# every login and can no longer reach any of the screens being tested.
#
# Nothing was reported, either: db-check-preserved.sh only asked for a team,
# so account+team passed while the relationship that made the account useful
# was gone. It now checks the assignment too.
#
# The catalogue-from-scratch flow still has fixtures — every OTHER team is
# cleared exactly as before, and there are 100+ of them.
#
# ── WHAT THIS SCRIPT DOES NOT DO ────────────────────────────────────────
#
# It never creates, deletes or edits a user, never touches a password, and
# never reseeds. It normalises ASSIGNMENT STATE only, and repairs a missing
# team for a preserved account. Demo cohorts and their submitted forms are
# left alone — teams that have submitted are exactly what the lecturer queue
# is supposed to show.
#
# Usage:  scripts/qa-seed-normalize.sh [path/to/FinalProjectDB.db]

set -euo pipefail

SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT="$( cd -- "$SCRIPT_DIR/.." &> /dev/null && pwd )"
DB="${1:-$ROOT/Server/FinalProjectDB.db}"

[ -f "$DB" ] || { echo "ERROR: database not found at $DB" >&2; exit 1; }

# Refuse to run against anything that is not the local dev file, so this can
# never be pointed at a copy someone cares about by a stray argument.
case "$DB" in
    *FinalProjectDB.db) ;;
    *) echo "ERROR: refusing to normalise '$DB' — not a FinalProjectDB.db." >&2; exit 1 ;;
esac

q() { sqlite3 -noheader "$DB" "$1"; }

# ── 0. Safety copy ───────────────────────────────────────────────────────
BACKUP_DIR="$ROOT/.local/db-backups"
mkdir -p "$BACKUP_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
cp "$DB" "$BACKUP_DIR/FinalProjectDB.db.$STAMP.pre-normalize.bak"
echo "Backup: .local/db-backups/FinalProjectDB.db.$STAMP.pre-normalize.bak"
echo ""

# ── 1. PRESERVE ACCOUNT — verify, never create ───────────────────────────
# If a preserved account is missing, something upstream deleted it and the
# right answer is to restore from a backup, not to invent a replacement with
# a new id that every foreign key would miss.
echo "[1/5] Verifying preserved accounts…"
STUDENT_EMAIL="noa.qa@motiva.local"
missing=0
for email in "$STUDENT_EMAIL" "avi.mentor.qa@motiva.local" "admin.qa@motiva.local"; do
    id=$(q "SELECT Id FROM Users WHERE Email='$email';")
    if [ -z "$id" ]; then
        echo "      MISSING: $email — restore it from a backup before continuing." >&2
        missing=1
    else
        roles=$(q "SELECT IFNULL(GROUP_CONCAT(Role),'(none)') FROM UserRoles WHERE UserId=$id;")
        echo "      ok  $email  (id=$id, roles=$roles)"
    fi
done
[ "$missing" -eq 0 ] || exit 1

# ── 2. PRESERVE TEAM — repair only when absent ───────────────────────────
# A Student must always have an active team. This does not rebuild history: if
# a team already exists the script leaves it exactly as it is.
echo "[2/5] Ensuring $STUDENT_EMAIL has an active team…"
SID=$(q "SELECT Id FROM Users WHERE Email='$STUDENT_EMAIL';")
TEAM=$(q "SELECT TeamId FROM TeamMembers WHERE UserId=$SID AND IsActive=1 LIMIT 1;")

if [ -n "$TEAM" ]; then
    echo "      ok  already in team $TEAM — left untouched."
else
    AY=$(q "SELECT IFNULL(AcademicYearId, (SELECT MIN(Id) FROM AcademicYears)) FROM Users WHERE Id=$SID;")
    sqlite3 "$DB" <<SQL
PRAGMA foreign_keys = ON;
BEGIN;
INSERT INTO Teams (AcademicYearId, IsExceptional, CreatedAt, TeamName)
VALUES ($AY, 0, datetime('now'), 'צוות QA');
INSERT INTO TeamMembers (TeamId, UserId, JoinedAt, IsActive, MemberRole)
VALUES (last_insert_rowid(), $SID, datetime('now'), 1, 'Student');
COMMIT;
SQL
    TEAM=$(q "SELECT TeamId FROM TeamMembers WHERE UserId=$SID AND IsActive=1 LIMIT 1;")
    echo "      repaired — created team $TEAM (the account had none)."
fi

# ── 3. PRESERVE PROJECT ASSIGNMENT — for the preserved team only ─────────
# Every OTHER team is cleared exactly as before, so the assignment flow can be
# exercised from its first step. Team-owned rows that only exist because a team
# HELD a project go with it; submitted forms do not, because a submitted form is
# an input to assignment, not a result of it.
#
# $TEAM — the preserved student's team, resolved in step 2 — is exempt from all
# of it. Every statement below states that exemption itself rather than relying
# on an earlier filter: this block is the one that manufactured the bug, and a
# single missed clause here silently strands the account again.
# Hard stop rather than an interpolated empty string. Without this, an empty
# $TEAM would render as "TeamId <> AND …" — a syntax error that aborts under
# `set -e`, which is safe but by luck. The exemption is the whole point of this
# block, so it is asserted before a single row is touched.
if [ -z "$TEAM" ]; then
    echo "ERROR: could not resolve the preserved student's team — refusing to clear" >&2
    echo "       assignment state, because nothing would be exempt." >&2
    exit 1
fi

echo "[3/5] Clearing assignment state (preserved team $TEAM exempt)…"
sqlite3 "$DB" <<SQL
PRAGMA foreign_keys = ON;
BEGIN;

DELETE FROM StudentSubTasks    WHERE TeamId <> $TEAM AND TeamId    IN (SELECT TeamId FROM Projects WHERE TeamId IS NOT NULL);
DELETE FROM TeamTasks          WHERE TeamId <> $TEAM AND TeamId    IN (SELECT TeamId FROM Projects WHERE TeamId IS NOT NULL);
DELETE FROM ProjectResources   WHERE TeamId <> $TEAM AND TeamId    IN (SELECT TeamId FROM Projects WHERE TeamId IS NOT NULL);
DELETE FROM ProjectTeamProfile WHERE ProjectId IN (SELECT Id FROM Projects WHERE TeamId IS NOT NULL AND TeamId <> $TEAM);

-- Back to the state the unassigned catalogue projects carry, so a freed
-- project is selectable again rather than sitting in a limbo status.
UPDATE Projects SET Status = 'Available' WHERE TeamId IS NOT NULL AND TeamId <> $TEAM AND Status IN ('InProgress','Active');
UPDATE Projects SET TeamId = NULL        WHERE TeamId IS NOT NULL AND TeamId <> $TEAM;

UPDATE AssignmentSettings SET AssignmentsPublished = 0, PublishedAt = NULL;

COMMIT;
SQL
echo "      assigned projects = $(q 'SELECT COUNT(*) FROM Projects WHERE TeamId IS NOT NULL;')  (expected: 1, the preserved team)"
echo "      assignments published = $(q 'SELECT IFNULL(MAX(AssignmentsPublished),0) FROM AssignmentSettings;')"

# ── 4. PRESERVE PROJECT ASSIGNMENT — report, never invent ────────────────
# Same rule as step 1 applies to accounts: if the relationship is already gone
# when this runs, the answer is to copy it back from a backup that still has
# it, not to hand the team whichever project happens to be free. Reported
# loudly and non-fatally, because the rest of the normalisation is still valid.
echo "[4/5] Verifying the preserved project assignment…"
HELD=$(q "SELECT Id FROM Projects WHERE TeamId=$TEAM;")
if [ -n "$HELD" ]; then
    echo "      ok  team $TEAM holds project $HELD — left untouched."
else
    echo "      MISSING: team $TEAM holds no project."
    echo "               Run scripts/qa-restore-preserved-assignment.sh to copy the"
    echo "               relationship back from .local/db-backups/."
fi

# ── 5. Inventory + integrity ─────────────────────────────────────────────
echo "[5/5] QA seed inventory"
echo ""
sqlite3 -header -column "$DB" "
SELECT t.Id, IIF(t.TeamName='','(unnamed)',t.TeamName) AS team,
       (SELECT COUNT(*) FROM TeamMembers m WHERE m.TeamId=t.Id AND m.IsActive=1) AS members,
       (SELECT COUNT(*) FROM TeamProjectPreferences p WHERE p.TeamId=t.Id)       AS prefs,
       (SELECT COUNT(*) FROM AssignmentFormSubmissions s WHERE s.TeamId=t.Id)    AS submitted,
       IFNULL((SELECT GROUP_CONCAT(pr.Id) FROM Projects pr WHERE pr.TeamId=t.Id),'-') AS project
FROM Teams t ORDER BY t.Id;"
echo ""
echo "users=$(q 'SELECT COUNT(*) FROM Users;')  teams=$(q 'SELECT COUNT(*) FROM Teams;')  projects=$(q 'SELECT COUNT(*) FROM Projects;')"
echo "submissions=$(q 'SELECT COUNT(*) FROM AssignmentFormSubmissions;')  assigned=$(q 'SELECT COUNT(*) FROM Projects WHERE TeamId IS NOT NULL;')  published=$(q 'SELECT IFNULL(MAX(AssignmentsPublished),0) FROM AssignmentSettings;')"
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
