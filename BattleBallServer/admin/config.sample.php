<?php
// Copy to config.php (kept out of version control) or use the BB_DB_HOST / BB_DB_PORT / BB_DB_NAME / BB_DB_USER / BB_DB_PASS
// and BB_ADMIN_IPS environment variables. Use a dedicated database user, never the MariaDB root account, in production.
return [
    'db_host' => '127.0.0.1',
    'db_port' => '3306',
    'db_name' => 'battleball',
    'db_user' => 'battleball_admin',
    'db_pass' => 'CHANGE_ME',
    'allowed_ips' => ['127.0.0.1', '::1'],   // the admin page answers only to these addresses
];
