<?php
// BattleBall admin: login and logout.
declare(strict_types=1);

/** Failed tries in the last hour (table web_login_attempt, V053; the web login page writes the same table): 10 per IP or 30 per user name stop the login. */
function login_limited(PDO $pdo, string $ip, string $username): bool
{
    $st = $pdo->prepare('SELECT COUNT(*) FROM web_login_attempt WHERE ip = ? AND created_at > (NOW() - INTERVAL 1 HOUR)');
    $st->execute([$ip]);
    if ((int)$st->fetchColumn() >= 10) {
        return true;
    }
    $st = $pdo->prepare('SELECT COUNT(*) FROM web_login_attempt WHERE username = ? AND created_at > (NOW() - INTERVAL 1 HOUR)');
    $st->execute([$username]);
    return (int)$st->fetchColumn() >= 30;
}

function page_login(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $u = trim((string)($_POST['username'] ?? ''));
        $pw = (string)($_POST['password'] ?? '');
        $ip = $_SERVER['REMOTE_ADDR'] ?? '0.0.0.0';
        if (login_limited($pdo, $ip, $u)) {
            flash('Bạn đã thử sai quá nhiều lần, hãy quay lại sau.', 'err');
            redirect('?p=login');
        }
        $st = $pdo->prepare("SELECT account_id, username, password_hash FROM account WHERE username = ? AND role = 'ADMIN' AND banned = 0");
        $st->execute([$u]);
        $row = $st->fetch();
        if ($row && password_verify($pw, $row['password_hash'])) {
            session_regenerate_id(true);
            $_SESSION['admin'] = $row['username'];
            $pdo->prepare('UPDATE account SET last_login_at = NOW() WHERE account_id = ?')->execute([$row['account_id']]);
            redirect('?p=home');
        }
        $pdo->prepare('INSERT INTO web_login_attempt (ip, username) VALUES (?, ?)')->execute([$ip, substr($u, 0, 64)]);
        usleep(700000);   // slow down guessing
        flash('Sai tên đăng nhập hoặc mật khẩu.', 'err');
        redirect('?p=login');
    }
    layout_start('Đăng nhập');
    echo '<h1>BattleBall Admin</h1><form method="post" action="?p=login" class="card">' . csrf_field()
        . '<label>Tên đăng nhập<input type="text" name="username" autocomplete="username" required></label>'
        . '<label>Mật khẩu<input type="password" name="password" autocomplete="current-password" required></label>'
        . '<button type="submit">Đăng nhập</button></form>';
    layout_end();
}

function page_logout(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $_SESSION = [];
        session_destroy();
    }
    redirect('?p=login');
}

register_hidden('logout', 'page_logout', 'home');
