<?php
// BattleBall admin: single entry point. Run locally with:  php -S 127.0.0.1:8080 -t admin   (set BB_DB_* first, see config.sample.php)
declare(strict_types=1);

require __DIR__ . '/lib.php';
require __DIR__ . '/pages.php';
require __DIR__ . '/mail_pages.php';

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

$grids = grid_specs($pdo);
$routes = [
    'home' => 'page_home', 'logout' => 'page_logout', 'skins' => 'page_skins', 'skin' => 'page_skin',
    'supports' => 'page_supports', 'monsters' => 'page_monsters', 'support' => 'page_support', 'support_new' => 'page_support_new', 'audit' => 'page_audit',
    'mail' => 'page_mail', 'notices' => 'page_notices',
];
if (isset($grids[$page])) {
    grid_page($pdo, $grids[$page], $page);
} elseif (isset($routes[$page])) {
    $routes[$page]($pdo);
} else {
    http_response_code(404);
    layout_start('Không tìm thấy');
    echo '<h1>Không tìm thấy trang</h1>';
    layout_end();
}
