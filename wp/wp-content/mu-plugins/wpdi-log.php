<?php
/**
 * Plugin Name: WPDI Event Log (minimal)
 * Description: Minimal REST-addressable CPT for client-side logging.
 * Version: 1.0.0
 */
if (!defined('ABSPATH')) exit;

add_action('init', function () {
    register_post_type('wpdi-event', [
        'labels'        => ['name' => 'WPDI Events', 'singular_name' => 'WPDI Event'],
        'public'        => false,
        'show_ui'       => true,        // set false if you don't want an admin menu
        'show_in_rest'  => true,
        'rest_base'     => 'wpdi-events',
        'supports'      => ['title','editor','author','revisions'],
        'map_meta_cap'  => true,
        'capability_type'=> 'post',
    ]);

    // Minimal meta schema so your API client can attach structured facts
    foreach ([
        'target_type'        => 'string',
        'action'             => 'string',  // create|update|restore|delete|conflict
        'old_modified_gmt'   => 'string',
        'new_modified_gmt'   => 'string',
        'old_revision_id'    => 'integer',
        'new_revision_id'    => 'integer',
        'payload_sha256'     => 'string',
        'client_correlation' => 'string',
    ] as $key => $type) {
        register_post_meta('wpdi-event', $key, [
            'type'         => $type,
            'single'       => true,
            'show_in_rest' => true,     // default schema is fine
            'auth_callback'=> function() { return current_user_can('edit_posts'); },
        ]);
    }
});

