<?php
/**
 * Plugin Name: Social OAuth Login
 * Description: Enables Google and GitHub OAuth login & registration on WP login screens,
 *              styled with Bootstrap via esm.sh and custom brand colors, with HEREDOC
 *              separators. Includes enhanced error handling and account linking.
 * Version:     1.9.0
 * Author:      Your Name
 */

defined('ABSPATH') || exit;

// -----------------------------------------------------------------------------
// CONFIG: Fixed redirect URI (must match OAuth provider settings)
// -----------------------------------------------------------------------------
if ( ! defined('SOL_OAUTH_REDIRECT_URI') ) {
    define('SOL_OAUTH_REDIRECT_URI', site_url('wp-login.php'));
}

// -----------------------------------------------------------------------------
// HELPERS (lazy, only used during login/register)
// -----------------------------------------------------------------------------
function sol_is_login_request(): bool {
    if (defined('WP_CLI') && WP_CLI) return false;
    if (wp_doing_cron() || wp_doing_ajax()) return false;
    if (defined('REST_REQUEST') && REST_REQUEST) return false;
    $script = $_SERVER['SCRIPT_NAME'] ?? '';
    return (substr($script, -14) === '/wp-login.php') || (basename($script) === 'wp-login.php');
}

function sol_env($key) {
    // Read from getenv/$_SERVER/$_ENV or a constant of the same name.
    $v = getenv($key);
    if ($v === false && isset($_SERVER[$key])) $v = $_SERVER[$key];
    if ($v === false && isset($_ENV[$key]))    $v = $_ENV[$key];
    if ($v === false && defined($key))         $v = constant($key);
    return $v ?: false;
}

function sol_get_creds(string $provider): array {
    $p = strtoupper($provider);
    return [
        'id'     => sol_env("{$p}_CLIENT_ID"),
        'secret' => sol_env("{$p}_CLIENT_SECRET"),
    ];
}

function sol_require_creds_or_redirect(string $provider) {
    $c = sol_get_creds($provider);
    if ($c['id'] && $c['secret']) return $c;

    // Log only on actual login requests to avoid spam
    if (sol_is_login_request()) {
        error_log('Social OAuth Login: Missing OAuth config for ' . $provider . ' ' . json_encode([
            strtolower("{$provider}_client_id")     => (bool) $c['id'],
            strtolower("{$provider}_client_secret") => (bool) $c['secret'],
        ]));
    }

    // Bounce back to login with a friendly error
    $login_url = add_query_arg(
        ['auth_error' => 'missing_config', 'auth_provider' => rawurlencode($provider)],
        wp_login_url()
    );
    wp_safe_redirect($login_url);
    exit;
}

// -----------------------------------------------------------------------------
// CRYPTO HELPERS FOR PKCE + STATE
// -----------------------------------------------------------------------------
function sol_b64url(string $bin): string {
    return rtrim(strtr(base64_encode($bin), '+/', '-_'), '=');
}

function sol_make_pkce(): array {
    $verifier  = sol_b64url(random_bytes(32)); // RFC 7636
    $challenge = sol_b64url(hash('sha256', $verifier, true));
    return [$verifier, $challenge];
}

/**
 * Create per-request state + nonce, persist a short-lived record so we can validate.
 * Returns [state, nonce].
 */
function sol_make_state(string $provider, string $pkce_verifier): array {
    $nonce = bin2hex(random_bytes(16));
    $state_payload = [
        'p'     => $provider,
        'ts'    => time(),
        'nonce' => $nonce,
    ];
    $state = sol_b64url(json_encode($state_payload));

    set_transient('sol_state_' . md5($state), [
        'provider' => $provider,
        'verifier' => $pkce_verifier,
        'ts'       => time(),
        'nonce'    => $nonce,
    ], 10 * MINUTE_IN_SECONDS);

    return [$state, $nonce];
}

function sol_consume_state_record(string $state) {
    $key = 'sol_state_' . md5($state);
    $record = get_transient($key);
    if ($record) {
        delete_transient($key); // one-time use
    }
    return $record ?: null;
}

// -----------------------------------------------------------------------------
// ENQUEUE STYLES (login only) with Bootstrap from esm.sh
// -----------------------------------------------------------------------------
add_action('login_enqueue_scripts', function () {
    wp_enqueue_style('sol-bootstrap-css', 'https://esm.sh/bootstrap@5.3.0/dist/css/bootstrap.min.css', [], null);
}, 20);

// -----------------------------------------------------------------------------
// RENDER LOGIN BUTTONS + SEPARATOR
// -----------------------------------------------------------------------------
add_filter('login_message', function ($message) {
    $action = $_GET['action'] ?? 'login';
    if (!in_array($action, ['login', 'register'], true)) return $message;

    $gurl = sol_get_oauth_url('google');
    $hurl = sol_get_oauth_url('github');

    $html  = "<p><a class='btn btn-primary w-100 mb-2' href='" . esc_url($gurl) . "'>Continue with Google</a></p>";
    $html .= "<p><a class='btn btn-dark w-100 mb-2'   href='" . esc_url($hurl) . "'>Continue with GitHub</a></p>";
    $html .= "<div class='d-flex align-items-center my-3'><hr class='flex-grow-1'/><span class='mx-2 text-muted'>OR</span><hr class='flex-grow-1'/></div>";
    return $html . $message;
});

// -----------------------------------------------------------------------------
// BUILD OAUTH URL (login screen) — PKCE + randomized state + NONCE for BOTH
// -----------------------------------------------------------------------------
function sol_get_oauth_url($provider) {
    $creds = sol_get_creds($provider);
    if (!$creds['id']) {
        // No hard redirect here; just show a link that returns with a message
        return add_query_arg(
            ['auth_error' => 'missing_config', 'auth_provider' => rawurlencode($provider)],
            wp_login_url()
        );
    }

    if ($provider === 'google') {
        [$verifier, $challenge] = sol_make_pkce();
        [$state, $nonce]        = sol_make_state('google', $verifier);

        $base   = 'https://accounts.google.com/o/oauth2/v2/auth';
        $params = [
            'client_id'             => $creds['id'],
            'redirect_uri'          => SOL_OAUTH_REDIRECT_URI,
            'response_type'         => 'code',
            'scope'                 => 'openid email profile',
            'state'                 => $state,
            'nonce'                 => $nonce,            // OIDC nonce
            'access_type'           => 'online',
            'prompt'                => 'consent',
            'code_challenge'        => $challenge,
            'code_challenge_method' => 'S256',
        ];
    } else {
        // GitHub: PKCE + randomized state (GitHub ignores nonce param)
        [$verifier, $challenge] = sol_make_pkce();
        [$state, $nonce]        = sol_make_state('github', $verifier);

        $base   = 'https://github.com/login/oauth/authorize';
        $params = [
            'client_id'             => $creds['id'],
            'redirect_uri'          => SOL_OAUTH_REDIRECT_URI,
            'scope'                 => 'read:user user:email',
            'state'                 => $state,
            'code_challenge'        => $challenge,
            'code_challenge_method' => 'S256',
        ];
    }

    $url = add_query_arg($params, $base);
    // Safe to log (contains client_id but not secret)
    error_log(sprintf('Social OAuth Login: Generated %s URL', $provider));
    return $url;
}

// -----------------------------------------------------------------------------
// LOGIN LOGGING: record who attempted to log in and status
// -----------------------------------------------------------------------------
function sol_log_login_attempt($provider, $email, $status, $extra = '') {
    $provider = sanitize_text_field($provider);
    $email    = sanitize_email($email ?: '');
    $status   = strtoupper(sanitize_text_field($status));
    $extra    = is_scalar($extra) ? sanitize_text_field((string) $extra) : '';

    $log_entry = sprintf(
        '[%s] Provider:%s | Email:%s | Status:%s | Extra:%s',
        gmdate('Y-m-d H:i:s'),
        $provider ?: 'unknown',
        $email ?: 'N/A',
        $status,
        $extra
    );
    error_log('Social OAuth Login Attempt: ' . $log_entry);
    // Optional dedicated file:
    // @file_put_contents(WP_CONTENT_DIR . '/sol-login.log', $log_entry . PHP_EOL, FILE_APPEND | LOCK_EX);
}

// -----------------------------------------------------------------------------
// HANDLE OAUTH CALLBACK (runs during wp-login.php only)
// -----------------------------------------------------------------------------
add_action('login_init', 'sol_handle_callback');
function sol_handle_callback() {
    // If an OAuth error came back, surface it nicely
    if (isset($_GET['error'])) {
        $err  = sanitize_text_field(wp_unslash($_GET['error']));
        $desc = isset($_GET['error_description']) ? sanitize_text_field(wp_unslash($_GET['error_description'])) : '';
        $login_url = add_query_arg(['auth_error' => $err, 'auth_error_desc' => rawurlencode($desc)], wp_login_url());
        wp_safe_redirect($login_url);
        exit;
    }

    if (empty($_GET['state']) || empty($_GET['code'])) return;

    $raw_state = sanitize_text_field(wp_unslash($_GET['state']));
    $code      = sanitize_text_field(wp_unslash($_GET['code']));

    // Validate randomized state for both providers and recover PKCE verifier + nonce
    $state_data = sol_consume_state_record($raw_state);
    $provider   = 'google'; // default assumption if state decoding fails
    $pkce_verifier = null;
    $expected_nonce = null;

    if ($state_data) {
        $provider       = $state_data['provider'] ?? 'google';
        $pkce_verifier  = $state_data['verifier'] ?? null;
        $expected_nonce = $state_data['nonce'] ?? null;

        // Optional freshness window (10 minutes)
        if (empty($state_data['ts']) || (time() - (int)$state_data['ts'] > 10 * MINUTE_IN_SECONDS)) {
            $login_url = add_query_arg(['auth_error' => 'state_expired'], wp_login_url());
            wp_safe_redirect($login_url);
            exit;
        }
    } else {
        // Back-compat: if a legacy static state comes through, treat it as provider flag
        $provider = sanitize_text_field(wp_unslash($_GET['state']));
    }

    // Ensure credentials exist (will redirect with error if not)
    $creds = sol_require_creds_or_redirect($provider);

    // Exchange code for token(s); pass PKCE verifier for both providers
    $token_resp = sol_exchange_code_for_token($provider, $code, $creds, $pkce_verifier);

    // Access token (and maybe id_token) expected
    $access_token = is_array($token_resp) ? ($token_resp['access_token'] ?? null) : $token_resp;

    if (!$access_token) {
        sol_log_login_attempt($provider, '', 'failure', 'no_token');
        error_log('Social OAuth Login: No token for ' . $provider);
        return;
    }

    // ---------- Google: verify ID token & nonce, then cross-check sub ----------
    if ($provider === 'google') {
        $id_token = is_array($token_resp) ? ($token_resp['id_token'] ?? null) : null;
        if (empty($id_token)) {
            sol_log_login_attempt('google', '', 'failure', 'missing_id_token');
            wp_safe_redirect(add_query_arg(['auth_error' => 'id_token_missing'], wp_login_url()));
            exit;
        }

        $claims = sol_verify_google_id_token($id_token, $creds['id'], $expected_nonce);
        if (is_wp_error($claims)) {
            sol_log_login_attempt('google', '', 'failure', $claims->get_error_code());
            wp_safe_redirect(add_query_arg(['auth_error' => $claims->get_error_code()], wp_login_url()));
            exit;
        }
    }
    // --------------------------------------------------------------------------

    $profile = sol_fetch_user_profile($provider, $access_token);
    error_log('Social OAuth Login: Retrieved profile for ' . $provider);

    // Normalize IDs
    if ($provider === 'google' && isset($profile->sub)) {
        $profile->id = $profile->sub;
    }

    // Verified email enforcement
    if ($provider === 'google') {
        $email_present  = isset($profile->email) && $profile->email;
        $email_verified = isset($profile->email_verified) ? (bool)$profile->email_verified : false;
        if (!$email_present || !$email_verified) {
            sol_log_login_attempt('google', $profile->email ?? '', 'failure', $email_present ? 'email_unverified' : 'email_required');
            $err = $email_present ? 'email_unverified' : 'email_required';
            wp_safe_redirect(add_query_arg(['auth_error' => $err, 'auth_provider' => 'google'], wp_login_url()));
            exit;
        }

        // Cross-check subject: ID token vs userinfo
        if (!empty($claims['sub']) && !empty($profile->sub) && $claims['sub'] !== $profile->sub) {
            sol_log_login_attempt('google', $profile->email ?? '', 'failure', 'sub_mismatch');
            wp_safe_redirect(add_query_arg(['auth_error' => 'sub_mismatch'], wp_login_url()));
            exit;
        }
    } elseif ($provider === 'github') {
        // Require a primary verified email (we attempt to fetch it in sol_fetch_user_profile)
        if (empty($profile->email)) {
            sol_log_login_attempt('github', '', 'failure', 'email_required');
            wp_safe_redirect(add_query_arg(['auth_error' => 'email_required', 'auth_provider' => 'github'], wp_login_url()));
            exit;
        }
    }

    if (empty($profile->id)) {
        sol_log_login_attempt($provider, $profile->email ?? '', 'failure', 'missing_profile_id');
        error_log('Social OAuth Login: Missing profile ID for ' . $provider);
        return;
    }

    $uid = sol_find_or_create_wp_user($provider, $profile);
    if (is_wp_error($uid)) {
        $code_err = $uid->get_error_code(); // e.g. 'email_not_approved'
        $email    = sanitize_email($profile->email ?? '');

        sol_log_login_attempt($provider, $email, 'failure', $code_err);

        $login_url = add_query_arg(
            ['auth_error' => $code_err, 'auth_email' => rawurlencode($email)],
            wp_login_url()
        );
        wp_safe_redirect($login_url);
        exit;
    }

    // Success
    sol_log_login_attempt($provider, $profile->email ?? '', 'success', 'user_id=' . $uid);
    wp_set_current_user($uid);
    wp_set_auth_cookie($uid);
    wp_redirect(home_url());
    exit;
}

// -----------------------------------------------------------------------------
// VERIFY GOOGLE ID TOKEN (server-side) using Google's tokeninfo endpoint
// Validates: signature (by Google), issuer, audience, expiry, and NONCE
// Returns claims array on success, WP_Error on failure.
// -----------------------------------------------------------------------------
function sol_verify_google_id_token(string $id_token, string $expected_aud, ?string $expected_nonce) {
    // Use tokeninfo for simplicity (lets Google validate signature/format)
    $resp = wp_remote_get(add_query_arg(['id_token' => $id_token], 'https://oauth2.googleapis.com/tokeninfo'), [
        'timeout' => 15,
    ]);
    if (is_wp_error($resp)) {
        return new WP_Error('id_token_http_error', $resp->get_error_message());
    }
    $code = wp_remote_retrieve_response_code($resp);
    $body = json_decode(wp_remote_retrieve_body($resp), true);
    if ($code !== 200 || !is_array($body)) {
        return new WP_Error('id_token_invalid', 'Invalid ID token response');
    }

    // Acceptable issuers per Google docs
    $iss = $body['iss'] ?? '';
    $ok_iss = in_array($iss, ['accounts.google.com', 'https://accounts.google.com'], true);
    if (!$ok_iss) {
        return new WP_Error('id_token_bad_iss', 'Bad issuer');
    }

    // Audience must match our client_id
    if (($body['aud'] ?? '') !== $expected_aud) {
        return new WP_Error('id_token_bad_aud', 'Bad audience');
    }

    // Expiry check
    $exp = isset($body['exp']) ? (int)$body['exp'] : 0;
    if ($exp <= time()) {
        return new WP_Error('id_token_expired', 'ID token expired');
    }

    // Nonce check (if we sent one)
    if ($expected_nonce && (($body['nonce'] ?? '') !== $expected_nonce)) {
        return new WP_Error('nonce_mismatch', 'Nonce mismatch');
    }

    // Return claims
    return $body;
}

// -----------------------------------------------------------------------------
// EXCHANGE CODE FOR TOKEN (no secrets in logs) — PKCE for BOTH providers
// -----------------------------------------------------------------------------
function sol_exchange_code_for_token($provider, $code, array $creds, $pkce_verifier = null) {
    $endpoint = ($provider === 'google')
        ? 'https://oauth2.googleapis.com/token'
        : 'https://github.com/login/oauth/access_token';

    $fields = [
        'code'          => $code,
        'client_id'     => $creds['id'],
        'client_secret' => $creds['secret'], // kept (confidential client)
    ];
    if ($provider === 'google') {
        $fields['redirect_uri'] = SOL_OAUTH_REDIRECT_URI;
        $fields['grant_type']   = 'authorization_code';
        if (!empty($pkce_verifier)) {
            $fields['code_verifier'] = $pkce_verifier; // PKCE
        }
    } else {
        // GitHub token exchange; supports PKCE via code_verifier
        $fields['redirect_uri'] = SOL_OAUTH_REDIRECT_URI;
        if (!empty($pkce_verifier)) {
            $fields['code_verifier'] = $pkce_verifier; // PKCE
        }
        // No grant_type required for GitHub
    }

    // Log without secrets
    $log_fields = $fields;
    unset($log_fields['client_secret'], $log_fields['code_verifier']);
    error_log('Social OAuth Login: Token request for ' . $provider . ': ' . json_encode($log_fields));

    $resp = wp_remote_post($endpoint, [
        'body'    => $fields,
        'headers' => ['Accept' => 'application/json'],
        'timeout' => 15,
    ]);
    if (is_wp_error($resp)) {
        error_log('Social OAuth Login: HTTP error: ' . $resp->get_error_message());
        return null;
    }
    $code_http = wp_remote_retrieve_response_code($resp);
    $body      = wp_remote_retrieve_body($resp);
    error_log('Social OAuth Login: Token response ' . $provider . ' HTTP ' . $code_http);

    $data = json_decode($body, true);
    return $data ?: null;
}

// -----------------------------------------------------------------------------
// FETCH USER PROFILE (accepts token string OR array for both providers)
// -----------------------------------------------------------------------------
function sol_fetch_user_profile($provider, $token) {
    $tokenStr = is_array($token) ? ($token['access_token'] ?? '') : $token;

    if ($provider === 'github') {
        $resp = wp_remote_get('https://api.github.com/user', [
            'headers' => ['Authorization' => 'token ' . $tokenStr, 'User-Agent' => 'WP-SOL'],
            'timeout' => 15,
        ]);
        $profile = json_decode(wp_remote_retrieve_body($resp));

        // If missing email, attempt to find a primary verified one
        if (empty($profile->email)) {
            $resp2 = wp_remote_get('https://api.github.com/user/emails', [
                'headers' => ['Authorization' => 'token ' . $tokenStr, 'User-Agent' => 'WP-SOL'],
                'timeout' => 15,
            ]);
            $emails = json_decode(wp_remote_retrieve_body($resp2), true);
            if (is_array($emails)) {
                foreach ($emails as $e) {
                    if (!empty($e['primary']) && !empty($e['verified']) && !empty($e['email'])) {
                        $profile->email = $e['email'];
                        break;
                    }
                }
            }
        }
        // Normalize id (GitHub has integer id)
        if (!isset($profile->id) && isset($profile->node_id)) {
            $profile->id = $profile->node_id;
        }
        return $profile;
    }

    $resp = wp_remote_get('https://www.googleapis.com/oauth2/v3/userinfo', [
        'headers' => ['Authorization' => 'Bearer ' . $tokenStr],
        'timeout' => 15,
    ]);
    return json_decode(wp_remote_retrieve_body($resp));
}

// -----------------------------------------------------------------------------
// FIND, LINK, OR CREATE WP USER (ALLOW DUPLICATE EMAILS)
// -----------------------------------------------------------------------------
function sol_find_or_create_wp_user($provider, $profile) {
    $profile_id     = sanitize_text_field($profile->id ?? '');
    $provider_login = "{$provider}_" . $profile_id;

    if ($user = get_user_by('login', $provider_login)) {
        return $user->ID;
    }

    $email = sanitize_email($profile->email ?? '');
    if (!empty($email) && function_exists('ael_is_email_approved') && !ael_is_email_approved($email)) {
        return new WP_Error('email_not_approved', 'This email address is not approved.');
    }

    $password = wp_generate_password();
    $uid      = wp_create_user($provider_login, $password, $email);
    if (is_wp_error($uid)) return $uid;

    if (!empty($profile->name)) {
        list($first, $last) = array_pad(explode(' ', sanitize_text_field($profile->name), 2), 2, '');
        wp_update_user([
            'ID'           => $uid,
            'first_name'   => $first,
            'last_name'    => $last,
            'display_name' => trim("$first $last"),
        ]);
    }
    if (!empty($profile->avatar_url)) {
        update_user_meta($uid, 'profile_picture', esc_url_raw($profile->avatar_url));
    }

    return $uid;
}

// -----------------------------------------------------------------------------
// SURFACE FRIENDLY ERRORS ON LOGIN SCREEN
// -----------------------------------------------------------------------------
add_filter('wp_login_errors', function (WP_Error $errors) {
    if (isset($_GET['auth_error']) && 'email_not_approved' === $_GET['auth_error']) {
        $email = isset($_GET['auth_email']) ? sanitize_email(wp_unslash($_GET['auth_email'])) : '';
        $msg   = $email
            ? sprintf(
                /* translators: %s = attempted email address */
                __('The email address <strong>%s</strong> is not approved for registration.', 'social-oauth-login'),
                esc_html($email)
            )
            : __('This email address is not approved for registration.', 'social-oauth-login');
        $errors->add('email_not_approved', wp_kses_post($msg));
    }

    if (isset($_GET['auth_error']) && 'missing_config' === $_GET['auth_error']) {
        $provider = isset($_GET['auth_provider']) ? esc_html(sanitize_text_field(wp_unslash($_GET['auth_provider']))) : '';
        $provider_label = $provider ? ucfirst($provider) : __('OAuth', 'social-oauth-login');
        $errors->add(
            'missing_config',
            wp_kses_post(sprintf(
                /* translators: %s = provider name */
                __('<strong>%s login isn’t available.</strong> The site’s OAuth credentials are not configured.', 'social-oauth-login'),
                $provider_label
            ))
        );
    }

    if (isset($_GET['auth_error']) && 'email_unverified' === $_GET['auth_error']) {
        $errors->add('email_unverified', wp_kses_post(
            __('<strong>Email not verified.</strong> Please verify your Google email and try again.', 'social-oauth-login')
        ));
    }

    if (isset($_GET['auth_error']) && 'email_required' === $_GET['auth_error']) {
        $prov = isset($_GET['auth_provider']) ? esc_html(sanitize_text_field(wp_unslash($_GET['auth_provider']))) : '';
        $msg  = $prov === 'github'
            ? __('<strong>Email required.</strong> Your GitHub account has no verified primary email.', 'social-oauth-login')
            : __('<strong>Email required.</strong> Your Google account did not return an email.', 'social-oauth-login');
        $errors->add('email_required', wp_kses_post($msg));
    }

    // New Google OIDC errors
    if (isset($_GET['auth_error']) && in_array($_GET['auth_error'], ['id_token_missing','id_token_http_error','id_token_invalid','id_token_bad_iss','id_token_bad_aud','id_token_expired','nonce_mismatch','sub_mismatch','state_expired'], true)) {
        $map = [
            'id_token_missing'   => __('<strong>Sign-in failed.</strong> Missing ID token from Google.', 'social-oauth-login'),
            'id_token_http_error'=> __('<strong>Sign-in failed.</strong> Could not verify ID token (network error).', 'social-oauth-login'),
            'id_token_invalid'   => __('<strong>Sign-in failed.</strong> Invalid ID token from Google.', 'social-oauth-login'),
            'id_token_bad_iss'   => __('<strong>Sign-in failed.</strong> Invalid issuer in ID token.', 'social-oauth-login'),
            'id_token_bad_aud'   => __('<strong>Sign-in failed.</strong> ID token not meant for this site.', 'social-oauth-login'),
            'id_token_expired'   => __('<strong>Sign-in failed.</strong> Your sign-in expired. Please try again.', 'social-oauth-login'),
            'nonce_mismatch'     => __('<strong>Sign-in failed.</strong> Nonce validation failed.', 'social-oauth-login'),
            'sub_mismatch'       => __('<strong>Sign-in failed.</strong> Identity could not be verified (mismatch).', 'social-oauth-login'),
            'state_expired'      => __('<strong>Sign-in expired.</strong> Please start again.', 'social-oauth-login'),
        ];
        $errors->add('oidc_error', wp_kses_post($map[$_GET['auth_error']]));
    }

    if (isset($_GET['auth_error']) && !in_array($_GET['auth_error'], ['email_not_approved','missing_config','email_unverified','email_required','id_token_missing','id_token_http_error','id_token_invalid','id_token_bad_iss','id_token_bad_aud','id_token_expired','nonce_mismatch','sub_mismatch','state_expired'], true)) {
        $desc = isset($_GET['auth_error_desc']) ? esc_html(sanitize_text_field(wp_unslash($_GET['auth_error_desc']))) : '';
        $errors->add('oauth_error', wp_kses_post(
            '<strong>OAuth error.</strong> ' . esc_html($_GET['auth_error']) . ($desc ? ' — ' . $desc : '')
        ));
    }

    return $errors;
}, 10, 1);
