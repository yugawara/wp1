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
peek(){ echo "$1" | sed -n '1,3p'; }      # show first few lines of a blob
hdr(){ echo "$1" | tr -d '\r' | sed -n '1,12p'; }

echo "== A) Root =="
html=$(curl -ks "$BASE/")
grep -qi "<!DOCTYPE html" <<<"$html" && ok "Root serves HTML" || { peek "$html"; fail "Root did not look like HTML"; }

echo "== B) REST index =="
# First look at status + headers (no body to jq if not 200)
resp_h=$(curl -ksI -H 'Accept: application/json' "$BASE/wp-json/")
code=$(printf '%s\n' "$resp_h" | awk 'NR==1{print $2}')
loc=$(printf '%s\n' "$resp_h" | grep -i '^Location:' || true)

case "$code" in
  200)
    body=$(curl -ks -H 'Accept: application/json' "$BASE/wp-json/")
    if jq -e 'has("namespaces")' <<<"$body" >/dev/null 2>&1; then
      ok "/wp-json/ returns JSON"
    else
      echo "--- /wp-json/ body (first lines) ---"; peek "$body"
      echo "--- /wp-json/ headers ---"; hdr "$resp_h"
      fail "/wp-json/ was not valid JSON"
    fi
    ;;
  301|308)
    echo "↪ /wp-json/ redirected ($code) $loc"
    # follow once
    target=$(printf '%s\n' "$loc" | awk '{print $2}')
    body=$(curl -ks -H 'Accept: application/json' "$target")
    if jq -e 'has("namespaces")' <<<"$body" >/dev/null 2>&1; then
      ok "redirect target returns JSON"
    else
      echo "--- redirect target body (first lines) ---"; peek "$body"
      echo "--- /wp-json/ headers ---"; hdr "$resp_h"
      fail "redirect target was not valid JSON"
    fi
    ;;
  *)
    echo "--- /wp-json/ headers ---"; hdr "$resp_h"
    fail "unexpected status from /wp-json/: $code"
    ;;
esac

echo "== C) /users/me (auth) =="
me=$(curl -ks -u "$USER:$PASS" -H 'Accept: application/json' "$BASE/wp-json/wp/v2/users/me/")
if jq -e '.id and .name' <<<"$me" >/dev/null 2>&1; then
  ok "users/me returns id+name"
else
  echo "--- users/me body ---"; peek "$me"
  fail "users/me bad"
fi

echo "== D) /settings (auth) =="
settings=$(curl -ks -u "$USER:$PASS" -H 'Accept: application/json' "$BASE/wp-json/wp/v2/settings/")
if jq -e '.title' <<<"$settings" >/dev/null 2>&1; then
  ok "settings returns title"
else
  echo "--- settings body ---"; peek "$settings"
  fail "settings bad"
fi

echo "== E) /settings (no slash) behavior =="
ns_h=$(curl -ksI "$BASE/wp-json/wp/v2/settings")
ns_code=$(printf '%s\n' "$ns_h" | awk 'NR==1{print $2}')
case "$ns_code" in
  301|308) ok "no-slash redirected ($ns_code)";;
  401|403) ok "no-slash returned auth status ($ns_code)";;
  *)
    echo "--- headers ---"; hdr "$ns_h"
    fail "expected 301/308/401/403, got $ns_code"
    ;;
esac

echo "== F) /settings (bad creds) =="
bc_code=$(curl -ks -o /dev/null -w '%{http_code}' \
          -u "$USER:DefinitelyWrongPassword123!" \
          -H 'Accept: application/json' "$BASE/wp-json/wp/v2/settings/")
[[ "$bc_code" == "401" || "$bc_code" == "403" ]] \
  && ok "bad creds return $bc_code" || fail "expected 401/403, got $bc_code"

echo "🎉 All checks passed"
