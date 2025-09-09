#!/usr/bin/env bash
set -euo pipefail

ROOT="${GITHUB_WORKSPACE:-$PWD}"
OUT_WWW="${ROOT}/BlazorWP/wwwroot/appsettings.json"
OUT_DEV="${ROOT}/BlazorWP/appsettings.Development.json"

echo "[DEBUG] ROOT=$ROOT"
echo "[DEBUG] OUT_WWW=$OUT_WWW"
echo "[DEBUG] OUT_DEV=$OUT_DEV"

mkdir -p "$(dirname "$OUT_WWW")" "$(dirname "$OUT_DEV")"

# If path exists but isn't a regular file (e.g., symlink), remove it so we can write a plain file.
for f in "$OUT_WWW" "$OUT_DEV"; do
  if [[ -L "$f" || ( -e "$f" && ! -f "$f" ) ]]; then
    echo "[DEBUG] Removing non-regular: $f"
    rm -f "$f"
  fi
done

payload() {
  cat <<JSON
{
  "WpBaseUrl": "${WP_BASE_URL:-http://127.0.0.1:8081}"
}
JSON
}

payload > "$OUT_WWW"
payload > "$OUT_DEV"

echo "[DEBUG] Wrote:"
ls -l "$OUT_WWW" "$OUT_DEV" || true
echo "[DEBUG] appsettings.json:"
sed -n '1,120p' "$OUT_WWW" || true
echo "[DEBUG] appsettings.Development.json:"
sed -n '1,120p' "$OUT_DEV" || true
