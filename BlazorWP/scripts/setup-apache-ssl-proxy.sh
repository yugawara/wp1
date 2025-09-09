#!/usr/bin/env bash
set -euo pipefail

# --- Config (override via env) ---
DOMAIN="${DOMAIN:-aspnet.lan}"
BACKEND="${BACKEND:-http://127.0.0.1:8081}"   # where PHP built-in server listens
LISTEN_PORT="${LISTEN_PORT:-8443}"
SRC_CERT_DIR="${SRC_CERT_DIR:-/srv/shared/aspnet/cert}"
AP_CERT_DIR="/etc/apache2/certs"
SITE_NAME="${SITE_NAME:-${DOMAIN}-ssl}"
VHOST_PATH="/etc/apache2/sites-available/${SITE_NAME}.conf"

echo "[INFO] DOMAIN=${DOMAIN}"
echo "[INFO] BACKEND=${BACKEND}"
echo "[INFO] LISTEN_PORT=${LISTEN_PORT}"
echo "[INFO] SRC_CERT_DIR=${SRC_CERT_DIR}"
echo "[INFO] SITE_NAME=${SITE_NAME}"

# --- Pre-flight checks ---
CRT="${SRC_CERT_DIR}/${DOMAIN}.crt"
KEY="${SRC_CERT_DIR}/${DOMAIN}.key"
if [[ ! -f "$CRT" || ! -f "$KEY" ]]; then
  echo "[ERROR] Missing cert files: $CRT or $KEY" >&2
  exit 1
fi

# --- Make sure the domain resolves locally on the runner ---
if ! grep -qE "[[:space:]]${DOMAIN}(\s|$)" /etc/hosts; then
  echo "127.0.0.1 ${DOMAIN}" | sudo tee -a /etc/hosts >/dev/null
  echo "::1       ${DOMAIN}" | sudo tee -a /etc/hosts >/dev/null
fi

# --- Install Apache if needed ---
if ! dpkg -s apache2 >/dev/null 2>&1; then
  echo "[INFO] Installing apache2..."
  sudo apt-get update -y
  sudo apt-get install -y apache2
fi

# --- Enable required modules (idempotent) ---
sudo a2enmod ssl proxy proxy_http headers http2 >/dev/null

# --- Ensure Apache listens on the desired port ---
if ! grep -q "Listen ${LISTEN_PORT}\b" /etc/apache2/ports.conf; then
  echo "Listen ${LISTEN_PORT}" | sudo tee -a /etc/apache2/ports.conf >/dev/null
fi

# --- Put certs where Apache can read them ---
sudo mkdir -p "${AP_CERT_DIR}"
sudo cp -f "$CRT" "${AP_CERT_DIR}/${DOMAIN}.crt"
sudo cp -f "$KEY" "${AP_CERT_DIR}/${DOMAIN}.key"
sudo chown root:root "${AP_CERT_DIR}/${DOMAIN}."*
sudo chmod 640 "${AP_CERT_DIR}/${DOMAIN}.key"
sudo chmod 644 "${AP_CERT_DIR}/${DOMAIN}.crt"

# --- Write the SSL vhost ---
sudo tee "${VHOST_PATH}" >/dev/null <<APACHECONF
<VirtualHost *:${LISTEN_PORT}>
    ServerName ${DOMAIN}

    SSLEngine on
    SSLCertificateFile ${AP_CERT_DIR}/${DOMAIN}.crt
    SSLCertificateKeyFile ${AP_CERT_DIR}/${DOMAIN}.key
    Protocols h2 http/1.1

    ProxyPreserveHost On
    RequestHeader set X-Forwarded-Proto "https"
    ProxyPass        / ${BACKEND}/
    ProxyPassReverse / ${BACKEND}/

    Header always set Strict-Transport-Security "max-age=31536000; includeSubDomains" env=HTTPS
    ErrorLog \${APACHE_LOG_DIR}/${SITE_NAME}_error.log
    CustomLog \${APACHE_LOG_DIR}/${SITE_NAME}_access.log combined
</VirtualHost>
APACHECONF

# --- Enable site (disable default ssl if present), test and reload ---
sudo a2dissite default-ssl >/dev/null 2>&1 || true
sudo a2ensite "${SITE_NAME}" >/dev/null
sudo apache2ctl configtest
sudo systemctl restart apache2 || sudo service apache2 restart

echo "[OK] Apache SSL proxy ready at https://${DOMAIN}:${LISTEN_PORT}/"
