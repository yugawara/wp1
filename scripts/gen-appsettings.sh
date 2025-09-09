#!/usr/bin/env bash
# scripts/gen-appsettings.sh
# Generate Blazor WASM wwwroot/appsettings.json from env for CI.
# Required: WP_BASE_URL

set -euo pipefail

: "${WP_BASE_URL:?WP_BASE_URL is required (e.g., http://127.0.0.1:8081)}"

write_json() {
  local target="$1"
  local content="{ \"WordPress\": { \"Url\": \"${WP_BASE_URL}\" } }"

  mkdir -p "$(dirname "$target")"

  if [[ "${DRY_RUN:-0}" == "1" ]]; then
    echo "[DRY-RUN] Would write to $target:"
    echo "$content"
  else
    printf '%s\n' "$content" > "$target"
    echo "Wrote $target -> ${WP_BASE_URL}"
  fi
}

# Always generate for BlazorWP
write_json "BlazorWP/wwwroot/appsettings.json"

# If you also ship a publish folder, generate there too
if [[ -d "blazor-publish" ]]; then
  write_json "blazor-publish/wwwroot/appsettings.json"
fi
