<?php
/**
 * Plugin Name: WPDI Locks via REST
 * Description: Expose WordPress native edit locks (_edit_lock, _edit_last) to REST for all post types.
 * Author: WpDI
 * Version: 0.1
 */

add_action('init', function () {
    // Loop over all post types that support REST
    foreach (get_post_types(['show_in_rest' => true], 'names') as $type) {
        register_post_meta($type, '_edit_lock', [
            'type'         => 'string',   // "timestamp:userId"
            'single'       => true,
            'show_in_rest' => true,
            'auth_callback'=> fn($allowed,$name,$post_id) => current_user_can('edit_post', $post_id),
        ]);
        register_post_meta($type, '_edit_last', [
            'type'         => 'integer',
            'single'       => true,
            'show_in_rest' => true,
            'auth_callback'=> fn($allowed,$name,$post_id) => current_user_can('edit_post', $post_id),
        ]);
    }
});
