#!/usr/bin/env bash
set -Eeuo pipefail

path_report() {
  local p="$DIR"
  echo "[PATH_REPORT] Target dir: $p"
  echo "[PATH_REPORT] Canonical: $(readlink -f "$DIR" || echo "(readlink -f failed)")"
  echo "[PATH_REPORT] Listing parents:"
  IFS='/' read -r -a parts <<< "$DIR"
  local cur="/"
  for part in "${parts[@]}"; do
    [[ -z "$part" ]] && continue
    cur="$cur$part"
    echo " - $(ls -ld --time-style='+%Y-%m-%d %H:%M:%S' "$cur" 2>&1 || echo "MISSING: $cur")"
    cur="$cur/"
  done
}

trap 'echo "[ERROR] line $LINENO: \"$BASH_COMMAND\" failed"
      echo; echo "[ERROR] PWD/ID:"; pwd; id
      echo; echo "[ERROR] Disk usage:"; df -h
      echo; echo "[ERROR] Env (subset):"; env | egrep -i "GITHUB_|WP_|DOTNET|PATH|HOME" || true
      echo; echo "[ERROR] Mounts:"; (mount || true) | sed -n "1,80p"
      echo; echo "[ERROR] Path walk:"; path_report
' ERR

ROOT="${GITHUB_WORKSPACE:-$PWD}"
OUT="${ROOT}/BlazorWP/wwwroot/appsettings.json"
DIR="$(dirname "$OUT")"

echo "[DEBUG] ROOT=$ROOT"
echo "[DEBUG] OUT=$OUT"
echo "[DEBUG] Ensuring directory: $DIR"

# Create directory two ways, then verify
mkdir -p -- "$DIR"
install -d -m 0755 "$DIR" 2>/dev/null || true

echo "[DEBUG] After mkdir/install:"
ls -ld "$DIR" || true
stat "$DIR" || true

if [[ ! -d "$DIR" ]]; then
  echo "[ERROR] Directory not found after creation attempts: $DIR" >&2
  path_report
  exit 1
fi
if [[ ! -w "$DIR" ]]; then
  echo "[ERROR] Directory is not writable: $DIR" >&2
  ls -ld "$DIR"
  id
  exit 1
fi

echo "[DEBUG] Listing BlazorWP tree before write:"
(ls -laR "${ROOT}/BlazorWP" || true) | sed -n '1,400p'

echo "[DEBUG] Writing file via tee…"
printf '%s\n' \
'{' \
"  \"WpBaseUrl\": \"${WP_BASE_URL:-http://127.0.0.1:8081}\"" \
'}' | tee "$OUT" >/dev/null

echo "[DEBUG] Wrote: $OUT"
ls -la "$DIR"
echo "[DEBUG] File content:"
sed -n '1,120p' "$OUT"

echo "[OK] gen-appsettings completed"
