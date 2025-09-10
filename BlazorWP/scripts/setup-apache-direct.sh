#!/usr/bin/env bash
# BlazorWP/scripts/setup-apache-direct.sh
set -euo pipefail

DOMAIN="${DOMAIN:-aspnet.lan}"
PORT="${PORT:-8443}"
WP_DIR="${WP_DIR:-$GITHUB_WORKSPACE/wordpress}"
CERT_DIR="${CERT_DIR:-$GITHUB_WORKSPACE/BlazorWP/cert}"

# Allow explicit overrides, otherwise use aspnet.lan.* generated files
CRT="${CERT_CERT_FILE:-$CERT_DIR/aspnet.lan.crt}"
KEY="${CERT_KEY_FILE:-$CERT_DIR/aspnet.lan.key}"
CHAIN="${CERT_CHAIN_FILE:-$CERT_DIR/aspnet.lan-ca.crt}"

echo "== setup-apache-direct =="
echo "DOMAIN=$DOMAIN  PORT=$PORT"
echo "WP_DIR=$WP_DIR"
echo "CERT_DIR=$CERT_DIR"
echo "CRT=$CRT"
echo "KEY=$KEY"
echo "CHAIN=$CHAIN"

[[ -d "$WP_DIR" ]] || { echo "Missing WP_DIR: $WP_DIR" >&2; exit 1; }
[[ -f "$CRT" && -f "$KEY" && -f "$CHAIN" ]] || {
  echo "Missing certs: $CRT / $KEY / $CHAIN" >&2; exit 1;
}

sudo apt-get update -y
sudo apt-get install -y apache2 libapache2-mod-php8.3
sudo a2enmod rewrite headers ssl mime dir
sudo chown -R "$USER":"$USER" "$GITHUB_WORKSPACE"

# Ensure the runner resolves aspnet.lan
if ! grep -qE "^[^#]*\s$DOMAIN(\s|$)" /etc/hosts; then
  echo "127.0.0.1  $DOMAIN" | sudo tee -a /etc/hosts >/dev/null
fi

# Default WordPress .htaccess for pretty permalinks/REST
cat > "$WP_DIR/.htaccess" <<'HT'
<IfModule mod_rewrite.c>
RewriteEngine On
RewriteBase /
RewriteRule ^index\.php$ - [L]
RewriteCond %{REQUEST_FILENAME} !-f
RewriteCond %{REQUEST_FILENAME} !-d
RewriteRule . /index.php [L]
</IfModule>
HT

# Listen on custom HTTPS port (avoid duplicate lines)
if ! grep -q "Listen $PORT" /etc/apache2/ports.conf; then
  echo "Listen $PORT" | sudo tee -a /etc/apache2/ports.conf >/dev/null
fi

# SSL vhost serving WordPress directly
sudo tee /etc/apache2/sites-available/wp-ssl.conf >/dev/null <<CONF
<VirtualHost *:${PORT}>
  ServerName ${DOMAIN}
  DocumentRoot "${WP_DIR}"

  SSLEngine on
  SSLCertificateFile      "${CRT}"
  SSLCertificateKeyFile   "${KEY}"
  SSLCertificateChainFile "${CHAIN}"

  # Pass Basic Auth header for WP Application Passwords
  SetEnvIf Authorization "(.*)" HTTP_AUTHORIZATION=\$1

  <Directory "${WP_DIR}">
    AllowOverride All
    Require all granted
    DirectoryIndex index.php
    Options -Indexes
  </Directory>

  <FilesMatch "\.php$">
    SetHandler application/x-httpd-php
  </FilesMatch>

  ErrorLog  \${APACHE_LOG_DIR}/wp-ssl-error.log
  CustomLog \${APACHE_LOG_DIR}/wp-ssl-access.log combined
</VirtualHost>
CONF

sudo a2ensite wp-ssl.conf
sudo a2dissite 000-default.conf || true
sudo systemctl restart apache2

# Ensure pretty permalinks so /wp-json/... just works
wp option update permalink_structure '/%postname%/' --path="$WP_DIR"
wp rewrite flush --hard --path="$WP_DIR"

echo "== Apache is serving WordPress at https://${DOMAIN}:${PORT}/ =="
