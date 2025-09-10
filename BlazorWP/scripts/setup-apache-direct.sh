#!/usr/bin/env bash
# BlazorWP/scripts/setup-apache-direct.sh
set -euo pipefail

# Inputs (sane defaults for your repo layout)
DOMAIN="${DOMAIN:-aspnet.lan}"
PORT="${PORT:-8443}"
WP_DIR="${WP_DIR:-$GITHUB_WORKSPACE/wordpress}"
CERT_DIR="${CERT_DIR:-$GITHUB_WORKSPACE/BlazorWP/cert/_shared}"

echo "== setup-apache-direct =="
echo "DOMAIN=$DOMAIN  PORT=$PORT"
echo "WP_DIR=$WP_DIR"
echo "CERT_DIR=$CERT_DIR"

# Basic sanity
[[ -d "$WP_DIR" ]] || { echo "Missing WP_DIR: $WP_DIR" >&2; exit 1; }
[[ -f "$CERT_DIR/dev.cert.pem" && -f "$CERT_DIR/dev.key.pem" && -f "$CERT_DIR/dev.ca.pem" ]] \
  || { echo "Missing certs in $CERT_DIR (dev.cert.pem/dev.key.pem/dev.ca.pem)" >&2; exit 1; }

# Install Apache + mod_php (8.3) and enable required modules
sudo apt-get update -y
sudo apt-get install -y apache2 libapache2-mod-php8.3
sudo a2enmod rewrite headers ssl mime dir
# make sure runner user owns workspace (avoid permission surprises)
sudo chown -R "$USER":"$USER" "$GITHUB_WORKSPACE"

# Ensure hosts resolution for the runner
if ! grep -qE "^[^#]*\s$DOMAIN(\s|$)" /etc/hosts; then
  echo "127.0.0.1  $DOMAIN" | sudo tee -a /etc/hosts >/dev/null
fi

# Write a default WordPress .htaccess (pretty permalinks + REST pretty routes)
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

# Listen on custom HTTPS port
if ! grep -q "Listen $PORT" /etc/apache2/ports.conf; then
  echo "Listen $PORT" | sudo tee -a /etc/apache2/ports.conf >/dev/null
fi

# Create SSL vhost serving WordPress directly
sudo tee /etc/apache2/sites-available/wp-ssl.conf >/dev/null <<CONF
<VirtualHost *:${PORT}>
  ServerName ${DOMAIN}
  DocumentRoot "${WP_DIR}"

  # TLS
  SSLEngine on
  SSLCertificateFile      "${CERT_DIR}/dev.cert.pem"
  SSLCertificateKeyFile   "${CERT_DIR}/dev.key.pem"
  SSLCertificateChainFile "${CERT_DIR}/dev.ca.pem"

  # Forward auth header (Application Passwords)
  SetEnvIf Authorization "(.*)" HTTP_AUTHORIZATION=\$1

  <Directory "${WP_DIR}">
    AllowOverride All
    Require all granted
    DirectoryIndex index.php
    Options -Indexes
  </Directory>

  # Ensure PHP files go through mod_php
  <FilesMatch "\.php$">
    SetHandler application/x-httpd-php
  </FilesMatch>

  ErrorLog  \${APACHE_LOG_DIR}/wp-ssl-error.log
  CustomLog \${APACHE_LOG_DIR}/wp-ssl-access.log combined
</VirtualHost>
CONF

# Enable site and restart Apache
sudo a2ensite wp-ssl.conf
sudo a2dissite 000-default.conf || true
sudo systemctl restart apache2

# Make sure pretty permalinks are active so REST pretty routes work
wp option update permalink_structure '/%postname%/' --path="$WP_DIR"
wp rewrite flush --hard --path="$WP_DIR"

echo "== Apache is serving WordPress at https://${DOMAIN}:${PORT}/ =="
