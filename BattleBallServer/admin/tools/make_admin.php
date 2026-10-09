<?php
// Creates or updates an ADMIN account:  php tools/make_admin.php <username> <password>
// The password is hashed with bcrypt cost 12 (same algorithm as the game accounts). Run it only on the server, never over a network.
declare(strict_types=1);
require __DIR__ . '/../lib.php';

if (PHP_SAPI !== 'cli') {
    exit("CLI only\n");
}
[$script, $user, $pw] = array_pad($argv, 3, '');
if (!preg_match('/^[a-z0-9_]{4,20}$/', $user)) {
    exit("Tên đăng nhập: 4 đến 20 ký tự a-z, 0-9, _\n");
}
if (strlen($pw) < 8 || strlen($pw) > 64) {
    exit("Mật khẩu: 8 đến 64 byte\n");
}
$hash = password_hash($pw, PASSWORD_BCRYPT, ['cost' => 12]);
$pdo = db();
$st = $pdo->prepare("INSERT INTO account (username, password_hash, role) VALUES (?,?, 'ADMIN') ON DUPLICATE KEY UPDATE password_hash = VALUES(password_hash), role = 'ADMIN'");
$st->execute([$user, $hash]);
echo "OK: tài khoản admin '$user' đã sẵn sàng\n";
