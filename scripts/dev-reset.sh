#!/usr/bin/env bash
# scripts/dev-reset.sh
#
# Local dev reset: stop the dev server, wipe ONLY build artifacts,
# rebuild the solution, and start the Server fresh.
#
# Use this after Client/Shared code changes when Blazor _framework
# hashes go stale and the browser starts 404'ing on old hashed
# .wasm/.pdb files. It does not touch the database, wwwroot uploads,
# git state, or source files.

set -euo pipefail

# ── Resolve repo root from this script's own location ────────────────────
SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT="$( cd -- "$SCRIPT_DIR/.." &> /dev/null && pwd )"
SLN="$ROOT/AuthWithAdmin.sln"

if [ ! -f "$SLN" ]; then
    echo "ERROR: $SLN not found. Run this script from inside the Gradify repo." >&2
    exit 1
fi

cd "$ROOT"

# ── 1. Stop running server processes ─────────────────────────────────────
echo "[1/4] Stopping AuthWithAdmin / dotnet run processes…"
pkill -f "AuthWithAdmin" 2>/dev/null || true
pkill -f "dotnet run"    2>/dev/null || true
sleep 2
if pgrep -fl "AuthWithAdmin" >/dev/null 2>&1; then
    echo "       Some processes were stubborn — sending SIGKILL…"
    pkill -9 -f "AuthWithAdmin" 2>/dev/null || true
    sleep 1
fi
echo "       Done."

# ── 2. Delete ONLY build artifacts under the three known project dirs ───
# Whitelist-guarded: a `case` ensures we never rm -rf an unexpected path,
# even if the variables get mangled. DB / wwwroot / .git / source are
# all outside these six paths and are untouched.
echo "[2/4] Deleting build artifacts (bin/ and obj/ only)…"
for proj in Client Server Shared; do
    for art in bin obj; do
        target="$ROOT/$proj/$art"
        case "$target" in
            "$ROOT/Client/bin"|"$ROOT/Client/obj"\
           |"$ROOT/Server/bin"|"$ROOT/Server/obj"\
           |"$ROOT/Shared/bin"|"$ROOT/Shared/obj")
                if [ -d "$target" ]; then
                    rm -rf -- "$target"
                    echo "       Removed $target"
                fi
                ;;
            *)
                echo "       REFUSED: unexpected path '$target' — skipped." >&2
                ;;
        esac
    done
done
echo "       Database, wwwroot, .git, source files: untouched."

# ── 3. dotnet clean ─────────────────────────────────────────────────────
echo "[3/5] dotnet clean $SLN"
dotnet clean "$SLN" >/dev/null

# ── 4. Build the solution ───────────────────────────────────────────────
echo "[4/5] dotnet build $SLN"
dotnet build "$SLN"

# ── 5. Start the Server (plain dotnet run --project) ───────────────────
echo ""
echo "[5/5] Starting Server"
echo ""
echo "       URLs:  https://localhost:7275"
echo "              http://localhost:5297"
echo ""
echo "       Hard-refresh the browser (⌘⇧R) once you see 'Application started.'"
echo ""

cd "$ROOT"
exec dotnet run --project Server/AuthWithAdmin.Server.csproj
