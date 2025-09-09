#!/usr/bin/env bash
set -euo pipefail

echo "[DEBUG] Current working directory:"
pwd
echo "[DEBUG] Listing current directory contents:"
ls -la
echo "[DEBUG] Listing repo root contents (one up):"
ls -la ..

# now the part that writes appsettings.json
cat > BlazorWP/wwwroot/appsettings.json <<'JSON'
{
  "WpBaseUrl": "'"${WP_BASE_URL:-http://127.0.0.1:8081}"'"
}
JSON
