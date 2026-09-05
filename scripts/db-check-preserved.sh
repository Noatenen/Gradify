#!/usr/bin/env bash
# scripts/db-check-preserved.sh
#
# Guards the ONE invariant a local DB reset keeps breaking:
#
#     an existing Student account with NO active team is TRAPPED.
#
# Why that combination is a dead end, and not merely untidy: a student with no
# team is routed to /create-team, and /create-team correctly refuses an email
# that already has an account. So the account can log in, is sent to the only
# screen that could help it, and that screen rejects it. There is no way out
# from inside the product.
#
# Preserving a user therefore means preserving the user's RELATIONSHIPS, not
# just the row in Users. A reset that keeps the account and drops its team
# membership has not preserved anything usable — it has manufactured the trap.
#
# THE SAME IS TRUE ONE LEVEL DOWN, and this script used to miss it. A student
# WITH a team whose team holds NO project is not stranded, but it is not the
# preserved account either: StudentStageService correctly resolves
# NeedsPreferences and every login lands on the project catalogue, so none of
# the Student screens the account exists to exercise can be reached. That is
# exactly what qa-seed-normalize.sh did to noa.qa, and this check passed on it
# because it only ever asked for a team. It now asks for the project too.
#
# This script never writes. It reports, and it exits non-zero when a PRESERVED
# account is stranded, so a reset that broke one cannot pass quietly.
#
# Usage:  scripts/db-check-preserved.sh [path/to/FinalProjectDB.db]

set -euo pipefail

SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT="$( cd -- "$SCRIPT_DIR/.." &> /dev/null && pwd )"
DB="${1:-$ROOT/Server/FinalProjectDB.db}"

if [ ! -f "$DB" ]; then
    echo "ERROR: database not found at $DB" >&2
    exit 1
fi

# ── The accounts that must survive a reset intact ────────────────────────
# Add to this list rather than remembering it. Anything here is checked for a
# full relationship graph, not just existence.
PRESERVED_EMAILS=(
    "noa.qa@motiva.local"
    "avi.mentor.qa@motiva.local"
    "admin.qa@motiva.local"
)

# Of those, the ones whose TEAM must also hold a PROJECT. Only a Student has a
# project through a team, and only a long-lived QA student is expected to be
# mid-journey rather than at its start — a freshly registered team legitimately
# holds nothing, so this is a named list and not a rule about all students.
PRESERVED_WITH_PROJECT=(
    "noa.qa@motiva.local"
)

q() { sqlite3 -noheader "$DB" "$1"; }

echo "── Preserved accounts ─────────────────────────────────────────────"
printf "%-28s %-5s %-14s %-14s %s\n" "EMAIL" "ID" "ROLES" "ACTIVE TEAMS" "PROJECT"

failed=0

# Whether $1 is in PRESERVED_WITH_PROJECT.
needs_project() {
    local e
    for e in "${PRESERVED_WITH_PROJECT[@]}"; do [ "$e" = "$1" ] && return 0; done
    return 1
}

for email in "${PRESERVED_EMAILS[@]}"; do
    id=$(q "SELECT Id FROM Users WHERE Email='$email';")

    if [ -z "$id" ]; then
        printf "%-28s %-5s %-14s %-14s %s\n" "$email" "-" "-" "*** ACCOUNT MISSING ***" "-"
        failed=1
        continue
    fi

    roles=$(q "SELECT IFNULL(GROUP_CONCAT(Role),'(none)') FROM UserRoles WHERE UserId=$id;")
    teams=$(q "SELECT IFNULL(GROUP_CONCAT(TeamId),'') FROM TeamMembers WHERE UserId=$id AND IsActive=1;")

    # Only a Student is trapped by having no team. A Mentor, Lecturer or Admin
    # is never routed to /create-team, so "no team" is their normal state and
    # flagging it would be noise.
    if [ -z "$teams" ]; then
        case "$roles" in
            *Student*)
                printf "%-28s %-5s %-14s %-14s %s\n" "$email" "$id" "$roles" "*** NONE — TRAPPED ***" "-"
                failed=1
                ;;
            *)
                printf "%-28s %-5s %-14s %-14s %s\n" "$email" "$id" "$roles" "none (expected)" "-"
                ;;
        esac
        continue
    fi

    # The project held by any of this user's active teams.
    project=$(q "SELECT IFNULL(GROUP_CONCAT(p.Id),'') FROM Projects p
                 WHERE p.TeamId IN (SELECT TeamId FROM TeamMembers
                                    WHERE UserId=$id AND IsActive=1);")

    if needs_project "$email" && [ -z "$project" ]; then
        printf "%-28s %-5s %-14s %-14s %s\n" "$email" "$id" "$roles" "$teams" "*** NONE — SENT TO CATALOGUE ***"
        failed=1
    else
        printf "%-28s %-5s %-14s %-14s %s\n" "$email" "$id" "$roles" "$teams" "${project:-none}"
    fi
done

# ── LEVEL 4: the work hanging off the project ────────────────────────────
#
# Account, team and project are the three levels this script grew to check, and
# all three passed on 2026-09-05 while the QA student's project held FIVE team
# tasks and nothing else. Its ProjectMilestones, Tasks and TaskSubmissions had
# gone with the reset, so the dashboard's attention card and upcoming
# deadlines, the Tasks page's filters and every submitted/pending state had no
# data to render — the account was usable and the screens it exists to exercise
# were not.
#
# A project with zero tasks is therefore a FAILURE, and reported as loudly as a
# missing team. Having no UPCOMING item is only a warning: a fixture's dates go
# stale as time passes, which is a reason to re-seed rather than a broken
# relationship.
echo ""
echo "── Project work on preserved projects ─────────────────────────────"
printf "%-28s %-7s %-7s %-9s %-9s %s\n" "EMAIL" "TASKS" "SUBS" "OVERDUE" "UPCOMING" "TEAMTASKS"

for email in "${PRESERVED_WITH_PROJECT[@]}"; do
    id=$(q "SELECT Id FROM Users WHERE Email='$email' LIMIT 1;")
    [ -n "$id" ] || continue

    pid=$(q "SELECT p.Id FROM Projects p
             WHERE p.TeamId IN (SELECT TeamId FROM TeamMembers
                                WHERE UserId=$id AND IsActive=1) LIMIT 1;")
    [ -n "$pid" ] || continue   # already reported as a missing project above

    tasks=$(q "SELECT COUNT(*) FROM Tasks WHERE ProjectId=$pid;")
    subs=$(q "SELECT COUNT(*) FROM TaskSubmissions s JOIN Tasks t ON t.Id=s.TaskId
              WHERE t.ProjectId=$pid;")
    # Open and past due — what the attention card and the overdue badge read.
    overdue=$(q "SELECT COUNT(*) FROM Tasks
                 WHERE ProjectId=$pid AND DueDate IS NOT NULL
                   AND date(DueDate) < date('now')
                   AND Status NOT IN ('Done','Completed');")
    upcoming=$(q "SELECT COUNT(*) FROM Tasks
                  WHERE ProjectId=$pid AND DueDate IS NOT NULL
                    AND date(DueDate) >= date('now');")
    teamtasks=$(q "SELECT COUNT(*) FROM TeamTasks
                   WHERE TeamId IN (SELECT TeamId FROM TeamMembers
                                    WHERE UserId=$id AND IsActive=1);")

    if [ "${tasks:-0}" -eq 0 ]; then
        printf "%-28s %-7s %-7s %-9s %-9s %s\n" \
            "$email" "*** 0 ***" "$subs" "$overdue" "$upcoming" "$teamtasks"
        failed=1
    else
        printf "%-28s %-7s %-7s %-9s %-9s %s\n" \
            "$email" "$tasks" "$subs" "$overdue" "$upcoming" "$teamtasks"
        [ "${upcoming:-0}" -eq 0 ] && echo "  WARN: no task is still ahead of today — the fixture's dates have gone stale."
    fi
done

# ── Everyone else in the same trap ───────────────────────────────────────
# The preserve list is who we PROMISED to keep. This catches the students we
# did not name but stranded anyway — user 2 in the Team 1 incident was exactly
# that, and nothing would have reported it.
echo ""
echo "── Other students with no active team ─────────────────────────────"

stranded=$(q "
    SELECT u.Id || '  ' || u.Email
    FROM Users u
    WHERE u.IsActive = 1
      AND EXISTS (SELECT 1 FROM UserRoles r WHERE r.UserId = u.Id AND r.Role = 'Student')
      AND NOT EXISTS (SELECT 1 FROM TeamMembers m WHERE m.UserId = u.Id AND m.IsActive = 1)
    ORDER BY u.Id;")

if [ -z "$stranded" ]; then
    echo "none"
else
    echo "$stranded"
    echo ""
    echo "Each of these can log in and will be sent to /create-team, which will"
    echo "then refuse their own email. Restore their team from a backup rather"
    echo "than creating a new one — see .local/db-backups/."
fi

echo ""
if [ "$failed" -ne 0 ]; then
    echo "FAIL: a preserved account lost the relationships that make it usable."
    echo "      Preserving the Users row alone does not satisfy the requirement."
    echo ""
    echo "      no team    → restore it from .local/db-backups/ (never create a new one)"
    echo "      no project → scripts/qa-restore-preserved-assignment.sh"
    echo "      no tasks   → scripts/qa-restore-preserved-tasks.sh"
    exit 1
fi

echo "OK: every preserved account still has the relationships its role needs."
