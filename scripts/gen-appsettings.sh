#!/usr/bin/env bash
set -euo pipefail

ROOT="${GITHUB_WORKSPACE:-$PWD}"
OUT="${ROOT}/BlazorWP/wwwroot/appsettings.json"
DIR="$(dirname "$OUT")"

echo "[DEBUG] ROOT=$ROOT"
echo "[DEBUG] OUT=$OUT"

# Ensure directory exists
mkdir -p "$DIR"

# If appsettings.json is a symlink (or anything non-regular), remove it
if [[ -L "$OUT" || ( -e "$OUT" && ! -f "$OUT" ) ]]; then
  echo "[DEBUG] Removing non-regular OUT ($OUT)"
  rm -f "$OUT"
fi

# Write a regular file (never follow symlinks)
printf '%s\n' \
'{' \
"  \"WpBaseUrl\": \"${WP_BASE_URL:-http://127.0.0.1:8081}\"" \
'}' > "$OUT"

echo "[DEBUG] Wrote: $OUT"
ls -l "$OUT" || true
sed -n '1,120p' "$OUT" || true
