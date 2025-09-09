#!/usr/bin/env bash
# scripts/gen-appsettings.sh
# Generate Blazor WASM wwwroot/appsettings.json from env for CI.
# Required: WP_BASE_URL (e.g., http://127.0.0.1:8081)
# Optional: PRECOMPRESS=1 to also write .gz/.br
# Optional: DRY_RUN=1 to only print what would be written.

set -euo pipefail

: "${WP_BASE_URL:?WP_BASE_URL is required}"

write_json() {
  local target="$1"
  local content
  read -r -d '' content <<JSON || true
{
  "WordPress": { "Url": "${WP_BASE_URL}" }
}
JSON

  if [[ "${DRY_RUN:-0}" == "1" ]]; then
    echo "[DRY-RUN] Would write to $target:"
    echo "$content"
  else
    mkdir -p "$(dirname "$target")"
    echo "$content" > "$target"
    echo "Wrote $target -> ${WP_BASE_URL}"
  fi
}

precompress_if_enabled() {
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

# Primary Blazor project
write_json "BlazorWP/wwwroot/appsettings.json"
precompress_if_enabled "BlazorWP/wwwroot/appsettings.json"

# Optional publish/static directory
if [[ -d "blazor-publish/wwwroot" ]]; then
  write_json "blazor-publish/wwwroot/appsettings.json"
  precompress_if_enabled "blazor-publish/wwwroot/appsettings.json"
fi
