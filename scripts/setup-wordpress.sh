#!/usr/bin/env bash
set -euo pipefail

### ── SAFETY GUARD: only run inside GitHub Actions ────────────────────────────
if [[ "${ALLOW_OUTSIDE_GHA:-0}" != "1" ]]; then
  if [[ "${GITHUB_ACTIONS:-}" != "true" ]] || [[ -z "${GITHUB_RUN_ID:-}" ]]; then
    echo "[ABORT] This script is locked to GitHub Actions. Set ALLOW_OUTSIDE_GHA=1 to bypass (at your own risk)."
    exit 42
  fi
fi

if [[ "${ALLOW_NON_UBUNTU:-0}" != "1" ]]; then
  if ! grep -qi 'ubuntu' /etc/os-release 2>/dev/null; then
    echo "[ABORT] Non-Ubuntu environment detected. Set ALLOW_NON_UBUNTU=1 to bypass."
    exit 43
  fi
fi

export DEBIAN_FRONTEND=noninteractive

### ── Config ──────────────────────────────────────────────────────────────────
WP_ROOT="/var/www/html/wordpress"
SITE_URL="http://localhost"
SITE_TITLE="CI Test Site"
ADMIN_USER="admin"
ADMIN_PASS="ChangeMe!"
ADMIN_EMAIL="admin@example.com"

DB_NAME="wordpress"
DB_USER="wpuser"
DB_PASS="StrongPass!"

CI_USER="ci-poster"
CI_EMAIL="ci-poster@example.com"

log() { echo -e "\n[+] $*"; }

### ── Install base packages ───────────────────────────────────────────────────
log "Updating apt and installing packages"
sudo apt-get update -y
sudo apt-get install -y \
  apache2 mariadb-server \
  php php-mysql libapache2-mod-php \
  php-cli php-curl php-gd php-mbstring php-xml php-zip \
  unzip wget curl ca-certificates jq

### ── Apache & DB basics ──────────────────────────────────────────────────────
log "Enabling Apache modules and starting services"
sudo a2enmod rewrite >/dev/null
sudo service apache2 start || true
sudo service mariadb start || sudo service mysql start || true

log "Creating database and user (idempotent)"
sudo mysql <<SQL
CREATE DATABASE IF NOT EXISTS \`${DB_NAME}\` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASS}';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL

### ── Fetch & place WordPress ─────────────────────────────────────────────────
log "Fetching WordPress (if not already present)"
if [[ ! -d "${WP_ROOT}" ]]; then
  mkdir -p /tmp/wpdl
  pushd /tmp/wpdl >/dev/null
  wget -q https://wordpress.org/latest.tar.gz
  tar xzf latest.tar.gz
  sudo mkdir -p "$(dirname "${WP_ROOT}")"
  sudo mv wordpress "${WP_ROOT}"
  popd >/dev/null
fi

log "Setting ownership & permissions"
sudo chown -R www-data:www-data "${WP_ROOT}"
sudo find "${WP_ROOT}" -type d -exec chmod 755 {} \;
sudo find "${WP_ROOT}" -type f -exec chmod 644 {} \;

### ── Apache vhost ────────────────────────────────────────────────────────────
log "Configuring Apache vhost"
sudo tee /etc/apache2/sites-available/wordpress.conf >/dev/null <<'EOF'
<VirtualHost *:80>
    ServerName localhost
    DocumentRoot /var/www/html/wordpress
    <Directory /var/www/html/wordpress>
        AllowOverride All
        Require all granted
    </Directory>
    ErrorLog ${APACHE_LOG_DIR}/wordpress_error.log
    CustomLog ${APACHE_LOG_DIR}/wordpress_access.log combined
</VirtualHost>
EOF

sudo a2ensite wordpress.conf >/dev/null || true
sudo a2dissite 000-default.conf >/dev/null || true
sudo service apache2 reload || true

### ── wp-config & WP-CLI ──────────────────────────────────────────────────────
log "Preparing wp-config.php (idempotent)"
if [[ ! -f "${WP_ROOT}/wp-config.php" ]]; then
  sudo -u www-data cp "${WP_ROOT}/wp-config-sample.php" "${WP_ROOT}/wp-config.php"
  sudo -u www-data sed -i \
    -e "s/database_name_here/${DB_NAME}/" \
    -e "s/username_here/${DB_USER}/" \
    -e "s/password_here/${DB_PASS}/" \
    "${WP_ROOT}/wp-config.php"
  sudo -u www-data bash -c "echo \"define('DISALLOW_FILE_EDIT', true);\" >> '${WP_ROOT}/wp-config.php'"
fi

log "Installing WP-CLI (if missing)"
if ! command -v wp >/dev/null 2>&1; then
  curl -fsSL -o /tmp/wp-cli.phar https://raw.githubusercontent.com/wp-cli/builds/gh-pages/phar/wp-cli.phar
  php /tmp/wp-cli.phar --info >/dev/null
  chmod +x /tmp/wp-cli.phar
  sudo mv /tmp/wp-cli.phar /usr/local/bin/wp
fi

### ── WP core install & setup ─────────────────────────────────────────────────
log "Running WordPress install via WP-CLI (idempotent)"
if ! sudo -u www-data wp core is-installed --path="${WP_ROOT}" >/dev/null 2>&1; then
  sudo -u www-data wp config shuffle-salts --path="${WP_ROOT}"
  sudo -u www-data wp core install \
    --path="${WP_ROOT}" \
    --url="${SITE_URL}" \
    --title="${SITE_TITLE}" \
    --admin_user="${ADMIN_USER}" \
    --admin_password="${ADMIN_PASS}" \
    --admin_email="${ADMIN_EMAIL}"
  sudo -u www-data wp rewrite structure '/%postname%/' --path="${WP_ROOT}"
  sudo -u www-data wp rewrite flush --hard --path="${WP_ROOT}"
fi

### ── CI user & Application Password ──────────────────────────────────────────
log "Ensuring CI user exists"
if ! sudo -u www-data wp user get "${CI_USER}" --field=ID --path="${WP_ROOT}" >/dev/null 2>&1; then
  sudo -u www-data wp user create "${CI_USER}" "${CI_EMAIL}" --role=author --user_pass="$(openssl rand -base64 18)" --path="${WP_ROOT}"
fi

log "Creating Application Password for CI user"
APP_PASS_LINE=$(sudo -u www-data wp user application-password create "${CI_USER}" "github-actions" --porcelain --path="${WP_ROOT}" || true)
if [[ -z "${APP_PASS_LINE}" ]]; then
  APP_PASS_LINE=$(sudo -u www-data wp user application-password list "${CI_USER}" --format=csv --path="${WP_ROOT}" | tail -n 1 | cut -d, -f1-1 || true)
fi

if [[ -n "${APP_PASS_LINE}" ]]; then
  echo "CI_APP_PASSWORD=${APP_PASS_LINE}"
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
      echo "wp_base_url=${SITE_URL}"
      echo "wp_user=${CI_USER}"
      echo "wp_app_password=${APP_PASS_LINE}"
    } >> "$GITHUB_OUTPUT"
  fi
else
  echo "WARN: Could not create/fetch Application Password; REST auth may fail."
fi

### ── Smoke check ─────────────────────────────────────────────────────────────
log "Smoke check: hitting site root"
curl -fsS "${SITE_URL}/" >/dev/null || echo "WARN: curl check failed; Apache may still be reloading."

log "Done. WordPress ready at ${SITE_URL} (DocumentRoot ${WP_ROOT})."
