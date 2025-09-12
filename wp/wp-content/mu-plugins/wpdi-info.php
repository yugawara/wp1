<?php
/**
 * Plugin Name: WPDI Info Meta (minimal)
 * Description: Adds REST-exposed object meta 'wpdi_info' to store structured, non-localized diagnostics for posts and office-cpt.
 * Version: 1.0.0
 */
if (!defined('ABSPATH')) exit;

add_action('init', function () {
    foreach (['post', 'office-cpt'] as $type) {
        register_post_meta($type, 'wpdi_info', [
            'object_subtype' => $type,
            'type'           => 'object',
            'single'         => true,
            'show_in_rest'   => [
                'schema' => [
                    'type'                 => 'object',
                    'additionalProperties' => true, // arbitrary JSON object
                ],
            ],
            'auth_callback'  => function() { return current_user_can('edit_posts'); },
            'default'        => [],
        ]);
    }
});

