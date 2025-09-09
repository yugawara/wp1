#!/usr/bin/env bash
set -euo pipefail

ROOT="${GITHUB_WORKSPACE:-$PWD}"
OUT="${ROOT}/BlazorWP/wwwroot/appsettings.json"
DIR="$(dirname "$OUT")"

echo "[DEBUG] ROOT=$ROOT"
echo "[DEBUG] OUT=$OUT"
echo "[DEBUG] Ensuring directory: $DIR"

# Create the directory (verbose), then verify it exists
mkdir -pv "$DIR"
if [[ ! -d "$DIR" ]]; then
  echo "[ERROR] Directory not found after mkdir: $DIR" >&2
  exit 1
fi

echo "[DEBUG] Writing file…"
cat > "$OUT" <<JSON
{
  "WpBaseUrl": "${WP_BASE_URL:-http://127.0.0.1:8081}"
}
JSON

echo "[DEBUG] Wrote: $OUT"
ls -la "$DIR"
