#!/usr/bin/env bash
set -euo pipefail

# --- Config (override via env) ---
DOMAIN="${DOMAIN:-aspnet.lan}"
DOMAIN_ALIASES="${DOMAIN_ALIASES:-wp.lan}"       # space-separated aliases
BACKEND="${BACKEND:-http://127.0.0.1:8081}"      # PHP built-in server
LISTEN_PORT="${LISTEN_PORT:-8443}"
SRC_CERT_DIR="${SRC_CERT_DIR:-/srv/shared/aspnet/cert}"
AP_CERT_DIR="/etc/apache2/certs"
SITE_NAME="${SITE_NAME:-${DOMAIN}-ssl}"
VHOST_PATH="/etc/apache2/sites-available/${SITE_NAME}.conf"

echo "[INFO] DOMAIN=${DOMAIN}"
echo "[INFO] DOMAIN_ALIASES=${DOMAIN_ALIASES}"
echo "[INFO] BACKEND=${BACKEND}"
echo "[INFO] LISTEN_PORT=${LISTEN_PORT}"
echo "[INFO] SRC_CERT_DIR=${SRC_CERT_DIR}"
echo "[INFO] SITE_NAME=${SITE_NAME}"

# --- Pre-flight checks ---
CRT="${SRC_CERT_DIR}/${DOMAIN}.crt"
KEY="${SRC_CERT_DIR}/${DOMAIN}.key"
if [[ ! -f "$CRT" || ! -f "$KEY" ]]; then
  echo "[ERROR] Missing cert files: $CRT or $KEY" >&2
  echo "[HINT] Keep DOMAIN=aspnet.lan (filenames) — SAN already includes wp.lan."
  exit 1
fi

# --- Ensure local resolution of primary + aliases ---
add_host() {
  local h="$1"
  grep -qE "[[:space:]]${h}(\s|$)" /etc/hosts || {
    echo "127.0.0.1 ${h}" | sudo tee -a /etc/hosts >/dev/null
    echo "::1       ${h}" | sudo tee -a /etc/hosts >/dev/null
  }
}
add_host "${DOMAIN}"
for a in ${DOMAIN_ALIASES}; do add_host "$a"; done

# --- Install Apache if needed ---
if ! dpkg -s apache2 >/dev/null 2>&1; then
  echo "[INFO] Installing apache2..."
  sudo apt-get update -y
  sudo apt-get install -y apache2
fi

# --- Enable required modules (idempotent) ---
sudo a2enmod ssl proxy proxy_http headers http2 >/dev/null

# --- Listen port ---
grep -q "Listen ${LISTEN_PORT}\b" /etc/apache2/ports.conf || \
  echo "Listen ${LISTEN_PORT}" | sudo tee -a /etc/apache2/ports.conf >/dev/null

# --- Certs into apache dir ---
sudo mkdir -p "${AP_CERT_DIR}"
sudo cp -f "$CRT" "${AP_CERT_DIR}/${DOMAIN}.crt"
sudo cp -f "$KEY" "${AP_CERT_DIR}/${DOMAIN}.key"
sudo chown root:root "${AP_CERT_DIR}/${DOMAIN}."*
sudo chmod 640 "${AP_CERT_DIR}/${DOMAIN}.key"
sudo chmod 644 "${AP_CERT_DIR}/${DOMAIN}.crt"

# --- Build ServerAlias line from aliases ---
SERVER_ALIAS_LINE=""
if [[ -n "${DOMAIN_ALIASES// }" ]]; then
  SERVER_ALIAS_LINE="ServerAlias ${DOMAIN_ALIASES}"
fi

# --- VHost with pretty REST passthrough ---
sudo tee "${VHOST_PATH}" >/dev/null <<APACHECONF
<VirtualHost *:${LISTEN_PORT}>
    ServerName ${DOMAIN}
    ${SERVER_ALIAS_LINE}

    SSLEngine on
    SSLCertificateFile ${AP_CERT_DIR}/${DOMAIN}.crt
    SSLCertificateKeyFile ${AP_CERT_DIR}/${DOMAIN}.key
    Protocols h2 http/1.1

    ProxyPreserveHost On
    RequestHeader set X-Forwarded-Proto "https"

    # Pretty REST → non-pretty for PHP built-in server
    ProxyPassMatch ^/wp-json/(.*)$ ${BACKEND}/index.php?rest_route=/$1

    # Everything else straight through
    ProxyPass        / ${BACKEND}/
    ProxyPassReverse / ${BACKEND}/

    Header always set Strict-Transport-Security "max-age=31536000; includeSubDomains" env=HTTPS
    ErrorLog \${APACHE_LOG_DIR}/${SITE_NAME}_error.log
    CustomLog \${APACHE_LOG_DIR}/${SITE_NAME}_access.log combined
</VirtualHost>
APACHECONF

sudo a2dissite default-ssl >/dev/null 2>&1 || true
sudo a2ensite "${SITE_NAME}" >/dev/null
sudo apache2ctl configtest
sudo systemctl restart apache2 || sudo service apache2 restart

echo "[OK] Apache SSL proxy ready at:"
echo "     • https://${DOMAIN}:${LISTEN_PORT}/"
for a in ${DOMAIN_ALIASES}; do
  echo "     • https://${a}:${LISTEN_PORT}/"
done
