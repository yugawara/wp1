#!/usr/bin/env bash
set -euo pipefail

OUT="BlazorWP/wwwroot/appsettings.json"
mkdir -p "$(dirname "$OUT")"     # <-- ensure the directory exists

echo "[DEBUG] writing to: $OUT"
cat > "$OUT" <<JSON
{
  "WpBaseUrl": "${WP_BASE_URL:-http://127.0.0.1:8081}"
}
JSON

echo "[DEBUG] wrote file; listing dir:"
ls -la "$(dirname "$OUT")"
