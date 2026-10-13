<?php
// BattleBall web: login. An ADMIN account goes straight to the admin page; a player account is only told that the password is right
// (the web has no player area: the game is played in the app). The answer for a wrong name or password is always the same sentence.
// Wrong tries are limited per IP and per user name (web_login_attempt).
declare(strict_types=1);
require __DIR__ . '/lib.php';
require __DIR__ . '/layout.php';
start_session();

$message = null;
$ok = false;
$username = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $username = trim((string)($_POST['username'] ?? ''));
    $password = (string)($_POST['password'] ?? '');
    try {
        $pdo = db();
        $ip = client_ip();
        if (!hash_equals(csrf_token(), (string)($_POST['_csrf'] ?? ''))) {
            $message = 'Phiên không hợp lệ, hãy tải lại trang.';
        } elseif (!empty($_POST['website'])) {
            usleep(500000);                                  // honeypot: bots fill every field
            $message = 'Sai tên đăng nhập hoặc mật khẩu.';
        } elseif (login_limited($pdo, $ip, $username)) {
            $message = 'Bạn đã thử sai quá nhiều lần, hãy quay lại sau.';
        } else {
            $row = null;
            if (valid_username($username) && valid_password($password)) {
                $st = $pdo->prepare('SELECT account_id, username, password_hash, role, banned FROM account WHERE username = ?');
                $st->execute([$username]);
                $row = $st->fetch() ?: null;
            }
            $good = $row !== null && password_verify($password, $row['password_hash']) && !$row['banned'];
            if (!$good) {
                if ($row === null) {
                    password_verify($password, '$2y$12$Jeuh7iCSWgQC.bH9J4l98Org9r28Wwe9SM/.86B0151MSs9yeIGP6');   // a throw-away hash: the same time as a real check
                }
                record_login_failure($pdo, $ip, $username);
                usleep(700000);                              // slow down guessing
                $message = 'Sai tên đăng nhập hoặc mật khẩu.';
            } elseif ($row['role'] === 'ADMIN') {
                start_admin_session($row['username']);
                $pdo->prepare('UPDATE account SET last_login_at = NOW() WHERE account_id = ?')->execute([$row['account_id']]);
                $target = (string)cfg('admin_url', 'admin/');
                header('Location: ' . (preg_match('~^(https?://|[A-Za-z0-9_./?=&-]+$)~', $target) ? $target : 'admin/'));
                exit;
            } else {
                $ok = true;
                $message = 'Tài khoản "' . $row['username'] . '" đúng. Trang web chưa có khu vực người chơi: hãy mở game và đăng nhập ở đó.';
                $username = '';
            }
        }
    } catch (Throwable $e) {
        error_log('login failed: ' . $e->getMessage());
        $message = 'Hệ thống đang bận, hãy thử lại sau.';
    }
}
web_header('Đăng nhập BattleBall', 'login');
?>
<main>
  <h1>Đăng nhập</h1>
  <p class="muted">Dùng tài khoản của game. Tài khoản quản trị sẽ vào thẳng trang quản trị.</p>
  <?php if ($message !== null): ?>
    <div class="msg <?= $ok ? 'ok' : 'err' ?>"><?= h($message) ?></div>
  <?php endif; ?>
  <?php if (!$ok): ?>
  <form method="post" action="login.php" autocomplete="off">
    <input type="hidden" name="_csrf" value="<?= h(csrf_token()) ?>">
    <label>Tên tài khoản
      <input type="text" name="username" value="<?= h($username) ?>" maxlength="20" required autocapitalize="none" spellcheck="false" autocomplete="username">
    </label>
    <label>Mật khẩu
      <input type="password" name="password" maxlength="64" required autocomplete="current-password">
    </label>
    <div class="hp" aria-hidden="true"><label>Website <input type="text" name="website" tabindex="-1" autocomplete="off"></label></div>
    <button type="submit">Đăng nhập</button>
  </form>
  <p class="muted">Chưa có tài khoản? <a href="register.php">Đăng ký</a></p>
  <?php endif; ?>
</main>
<?php web_footer();
