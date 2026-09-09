#!/usr/bin/env bash
# scripts/seed-final-demo-data.sh
#
# Applies the FINAL faculty-demo data seed (scripts/seed-final-demo-data.sql):
# reconnects the four existing demo teams/projects, distributes their mentors,
# repairs one membership, trims the pending-assignment board to exactly three,
# and adds a small "current" task refresh.
#
# It does NOT create a second dataset — it repairs and lightly refreshes the
# existing one. Real/QA data (users 1-18, team 1, project 1) is never touched.
#
# Idempotent: running it twice produces the same state and never duplicates.
#
# Usage:  scripts/seed-final-demo-data.sh [path/to/FinalProjectDB.db]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DB="${1:-$ROOT/Server/FinalProjectDB.db}"
SQL="$ROOT/scripts/seed-final-demo-data.sql"

case "$DB" in
    *FinalProjectDB.db) ;;
    *) echo "ERROR: refusing to seed '$DB' — not a FinalProjectDB.db." >&2; exit 1 ;;
esac
[ -f "$DB" ]  || { echo "ERROR: database not found: $DB" >&2; exit 1; }
[ -f "$SQL" ] || { echo "ERROR: seed SQL not found: $SQL" >&2; exit 1; }

# ── 1. Timestamped backup + SHA256 ──────────────────────────────────────────
BACKUP_DIR="$ROOT/.local/db-backups"
mkdir -p "$BACKUP_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
BK="$BACKUP_DIR/FinalProjectDB.db.$STAMP.pre-demo.bak"
cp "$DB" "$BK"
echo "Backup : $BK"
echo -n "SHA256 : "; sha256sum "$BK" | awk '{print $1}'

# ── 2. Pre-write integrity gates ────────────────────────────────────────────
echo "── foreign_key_check (pre) ──"; sqlite3 "$DB" "PRAGMA foreign_key_check;"
echo "── integrity_check   (pre) ──"; sqlite3 "$DB" "PRAGMA integrity_check;"

# ── 3. Apply the seed (the .sql wraps its own BEGIN/COMMIT) ──────────────────
sqlite3 "$DB" < "$SQL"

# ── 4. Post-write integrity gates ───────────────────────────────────────────
echo "── foreign_key_check (post) ──"; sqlite3 "$DB" "PRAGMA foreign_key_check;"
echo "── integrity_check   (post) ──"; sqlite3 "$DB" "PRAGMA integrity_check;"

echo "Done. Demo seed applied to: $DB"
