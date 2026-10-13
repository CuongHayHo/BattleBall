<?php
// Copy to config.php (kept out of version control) or use the BB_DB_HOST / BB_DB_PORT / BB_DB_NAME / BB_DB_USER / BB_DB_PASS environment variables.
// Use a dedicated database user that may only INSERT/SELECT on the player tables, never the MariaDB root account, on a real server.
return [
    'db_host' => '127.0.0.1',
    'db_port' => '3306',
    'db_name' => 'battleball',
    'db_user' => 'battleball_web',
    'db_pass' => 'CHANGE_ME',
    'download_url' => '',   // link to download the game (APK or store page); also BB_DOWNLOAD_URL
];
