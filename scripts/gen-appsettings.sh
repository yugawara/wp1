#!/usr/bin/env bash
set -euo pipefail

: "${WP_BASE_URL:?WP_BASE_URL is required (e.g., http://127.0.0.1:8081)}"

generate_file() {
  local target="$1"
  mkdir -p "$(dirname "$target")"
  local content="{ \"WordPress\": { \"Url\": \"${WP_BASE_URL}\" } }"

  if [[ "${DRY_RUN:-0}" == "1" ]]; then
    echo "[DRY-RUN] Would write to $target:"
    echo "$content"
  else
    echo "$content" > "$target"
    echo "Wrote $target -> ${WP_BASE_URL}"
  fi
}

compress_if_needed() {
  local target="$1"
  if [[ "${PRECOMPRESS:-0}" == "1" && "${DRY_RUN:-0}" != "1" ]]; then
    gzip -c9 "$target" > "${target}.gz"
    if command -v brotli >/dev/null 2>&1; then
      brotli -f -q 11 "$target"
    else
      echo "brotli not found; skipping .br for $target"
    fi
    echo "Precompressed $target"
  fi
}

# Blazor project
generate_file "BlazorWP/wwwroot/appsettings.json"
compress_if_needed "BlazorWP/wwwroot/appsettings.json"

# Optional publish folder
if [[ -d "blazor-publish" ]]; then
  generate_file "blazor-publish/wwwroot/appsettings.json"
  compress_if_needed "blazor-publish/wwwroot/appsettings.json"
fi
