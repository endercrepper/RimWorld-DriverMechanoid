#!/usr/bin/env bash
# Syncs all changes to GitHub. Run after any edit to the mod.
# Usage: GH_TOKEN=<your_token> ./Source/sync.sh "commit message"
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$HERE"

MSG="${1:-auto-sync: update mod files}"

if [ -z "${GH_TOKEN:-}" ]; then
  echo "ERROR: Set GH_TOKEN env var first." >&2
  echo "  export GH_TOKEN=github_pat_..." >&2
  exit 1
fi

# Clean build artifacts so they don't get committed.
rm -rf Source/bin Source/obj

git add -A
if git diff --cached --quiet; then
  echo "No changes to sync."
  exit 0
fi

git commit -m "$MSG" 2>&1 | tail -3
git push "https://endercrepper:${GH_TOKEN}@github.com/endercrepper/RimWorld-DriverMechanoid.git" main 2>&1 | tail -5
echo ">> Synced to GitHub."
