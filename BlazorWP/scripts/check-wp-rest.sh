#!/usr/bin/env bash
set -euo pipefail

: "${WP_BASE_URL:?Need WP_BASE_URL}"
: "${WP_USERNAME:?Need WP_USERNAME}"
: "${WP_APP_PASSWORD:?Need WP_APP_PASSWORD}"

BASE="$WP_BASE_URL"
USER="$WP_USERNAME"
PASS="$WP_APP_PASSWORD"

ok(){ echo "✅ $1"; }
fail(){ echo "❌ $1"; exit 1; }

echo "== A) Root =="
html=$(curl -ks "$BASE/")
grep -qi "<!DOCTYPE html" <<<"$html" && ok "Root serves HTML" || fail "Root did not look like HTML"

echo "== B) REST index =="
curl -ks -H 'Accept: application/json' "$BASE/wp-json/" | jq -e 'has("namespaces")' >/dev/null \
  && ok "/wp-json/ returns JSON" || fail "/wp-json/ missing namespaces"

echo "== C) /users/me (auth) =="
me=$(curl -ks -u "$USER:$PASS" -H 'Accept: application/json' "$BASE/wp-json/wp/v2/users/me/")
jq -e '.id and .name' <<<"$me" >/dev/null \
  && ok "users/me returns id+name" || { echo "$me"; fail "users/me bad"; }

echo "== D) /settings (auth) =="
settings=$(curl -ks -u "$USER:$PASS" -H 'Accept: application/json' "$BASE/wp-json/wp/v2/settings/")
jq -e '.title' <<<"$settings" >/dev/null \
  && ok "settings returns title" || { echo "$settings"; fail "settings bad"; }

echo "== E) /settings (no slash) behavior =="
code=$(curl -ks -o /dev/null -w '%{http_code}' "$BASE/wp-json/wp/v2/settings")
case "$code" in
  301|308) ok "no-slash redirected ($code)";;
  401|403) ok "no-slash returned auth status ($code)";;
  *)       fail "expected 301/308/401/403, got $code";;
esac

echo "== F) /settings (bad creds) =="
code=$(curl -ks -o /dev/null -w '%{http_code}' \
       -u "$USER:DefinitelyWrongPassword123!" \
       -H 'Accept: application/json' "$BASE/wp-json/wp/v2/settings/")
[[ "$code" == "401" || "$code" == "403" ]] \
  && ok "bad creds return $code" || fail "expected 401/403, got $code"

echo "🎉 All checks passed"

