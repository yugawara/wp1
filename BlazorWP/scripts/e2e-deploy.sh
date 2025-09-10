#!/usr/bin/env bash
set -euo pipefail

# Resolve paths relative to this script file
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BLAZORWP_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"          # .../BlazorWP
REPO_ROOT="$(cd "${BLAZORWP_DIR}/.." && pwd)"          # .../wp-yasuaki

# ===== Config (override via env) =====
APP_PROJECT="${APP_PROJECT:-${BLAZORWP_DIR}/BlazorWP.csproj}"
CONFIG="${CONFIG:-Release}"
OUT_DIR="${OUT_DIR:-${REPO_ROOT}/blazor-publish}"
BASE_HREF="${BASE_HREF:-/blazorapp/}"
TARGET_DIR="${TARGET_DIR:-/var/www/html/wordpress/blazorapp}"
PATCH_HTACCESS="${PATCH_HTACCESS:-1}"
USE_MSBUILD_BASE="${USE_MSBUILD_BASE:-0}"
WP_BASE_URL="${WP_BASE_URL:-http://localhost}"   # default if not set

# ===== Sanity =====
[[ "$BASE_HREF" == */ ]] || { echo "BASE_HREF must end with '/'. current: $BASE_HREF" >&2; exit 1; }

# ===== Publish =====
echo "==> Publishing $APP_PROJECT ($CONFIG) → $OUT_DIR"
if [[ "$USE_MSBUILD_BASE" == "1" ]]; then
  # bake base path into the build (no sed needed)
  dotnet publish "$APP_PROJECT" -c "$CONFIG" -o "$OUT_DIR" \
    -p:StaticWebAssetBasePath="${BASE_HREF#/}"   # drop leading slash for msbuild
else
  dotnet publish "$APP_PROJECT" -c "$CONFIG" -o "$OUT_DIR"
fi

INDEX="$OUT_DIR/wwwroot/index.html"
[[ -f "$INDEX" ]] || { echo "index.html not found at $INDEX" >&2; exit 1; }

# ===== Patch <base href> unless baked by MSBuild =====
if [[ "$USE_MSBUILD_BASE" != "1" ]]; then
  echo "==> Patching <base href> → $BASE_HREF"
  # Replace any existing base tag
  sed -i -E "s#<base[[:space:]]+href=\"[^\"]*\"[[:space:]]*/?>#<base href=\"${BASE_HREF}\" />#i" "$INDEX"
fi

# Show the result
grep -i '<base href' "$INDEX" || { echo "no <base href> found after publish/patch" >&2; exit 1; }

# ===== Prepare target dir (works for local wp.lan and CI runner) =====
echo "==> Ensuring target dir exists: $TARGET_DIR"
sudo mkdir -p "$TARGET_DIR"

# ===== Deploy static files =====
echo "==> Deploying to $TARGET_DIR"
sudo rsync -az --delete "$OUT_DIR/wwwroot/" "$TARGET_DIR/"

# ===== .htaccess for wasm MIME + SPA fallback =====
if [[ "$PATCH_HTACCESS" == "1" ]]; then
  echo "==> Writing .htaccess (wasm MIME + SPA fallback @ $BASE_HREF)"
  sudo tee "$TARGET_DIR/.htaccess" >/dev/null <<HT
AddType application/wasm .wasm

# SPA fallback under ${BASE_HREF}
RewriteEngine On
RewriteBase ${BASE_HREF}
RewriteCond %{REQUEST_FILENAME} !-f
RewriteCond %{REQUEST_FILENAME} !-d
RewriteRule ^ index.html [L]
HT
fi

echo "==> Deployed files (head):"
sudo ls -la "$TARGET_DIR" | sed -n '1,80p'

# ===== Simple curl test =====
APP_URL="${WP_BASE_URL%/}${BASE_HREF}"
echo "==> Testing deployment with curl: $APP_URL"
if curl -fsS -o /dev/null -w "%{http_code}\n" "$APP_URL" | grep -q "200"; then
  echo "✅ App responded successfully at $APP_URL"
else
  echo "❌ App did not respond as expected at $APP_URL"
  exit 1
fi

echo "✅ e2e-deploy done"
