<?php
// BattleBall admin: single entry point. Run locally with:  php -S 127.0.0.1:8080 -t admin   (set BB_DB_* first, see config.sample.php)
// Layout: lib.php (core and table screens), registry.php (menu and routes), layout.php (page frame), pages\*.php (one module per area;
// each one announces its screens with register_page / register_grids / register_link / register_hidden).
declare(strict_types=1);

require __DIR__ . '/lib.php';
require __DIR__ . '/registry.php';
require __DIR__ . '/layout.php';
foreach (['common', 'auth', 'home', 'skins', 'shopskins', 'grids_skin', 'supports', 'grids_item', 'monsters', 'grids_dungeon', 'grids_drop', 'mail', 'grids_system', 'audit'] as $module) {
    require __DIR__ . '/pages/' . $module . '.php';
}

boot();
csrf_check();

$page = (string)($_GET['p'] ?? 'home');
try {
    $pdo = db();
} catch (Throwable $e) {
    http_response_code(500);
    exit('Không kết nối được cơ sở dữ liệu: ' . h($e->getMessage()));
}

if ($page === 'login') {
    page_login($pdo);
    exit;
}
if (admin_name() === null) {
    redirect('?p=login');
}

$route = find_route($page);
if ($route === null) {
    http_response_code(404);
    layout_start('Không tìm thấy');
    echo '<h1>Không tìm thấy trang</h1><p><a href="?p=home">Về trang tổng quan</a></p>';
    layout_end();
} elseif (isset($route['provider'])) {
    grid_page($pdo, ($route['provider'])($pdo)[$page], $page);
} else {
    ($route['handler'])($pdo);
}
