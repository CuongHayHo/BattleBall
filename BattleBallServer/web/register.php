<?php
// BattleBall web registration (CONFIRMED design: no e-mail, username 4-20 a-z0-9_, password 8+, limit per IP and a captcha, bcrypt cost 12).
// It writes straight into the game database, with the same rows as the in-game registration.
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
    $password2 = (string)($_POST['password2'] ?? '');
    try {
        $pdo = db();
        $ip = client_ip();
        if (!hash_equals(csrf_token(), (string)($_POST['_csrf'] ?? ''))) {
            $message = 'Phiên không hợp lệ, hãy tải lại trang.';
        } elseif (!empty($_POST['website'])) {
            usleep(500000);                                  // honeypot: bots fill every field
            $message = 'Đăng ký không thành công.';
        } elseif (rate_limited($pdo, $ip)) {
            $message = 'Bạn đã thử quá nhiều lần, hãy quay lại sau.';
        } elseif (!check_captcha((string)($_POST['captcha'] ?? ''))) {
            record_attempt($pdo, $ip, false);
            $message = 'Mã xác nhận chưa đúng.';
        } elseif (!valid_username($username)) {
            record_attempt($pdo, $ip, false);
            $message = 'Tên tài khoản phải có 4 đến 20 ký tự gồm chữ thường a-z, số 0-9 và dấu gạch dưới.';
        } elseif (!valid_password($password)) {
            record_attempt($pdo, $ip, false);
            $message = 'Mật khẩu phải có từ 8 đến 64 byte.';
        } elseif ($password !== $password2) {
            record_attempt($pdo, $ip, false);
            $message = 'Hai mật khẩu không giống nhau.';
        } else {
            $r = create_account($pdo, $username, $password);
            record_attempt($pdo, $ip, $r === 'ok');
            if ($r === 'ok') {
                $ok = true;
                $message = 'Đã tạo tài khoản "' . $username . '". Hãy mở game và đăng nhập.';
                $username = '';
            } else {
                $message = 'Tên tài khoản đã có người dùng.';
            }
        }
    } catch (Throwable $e) {
        error_log('register failed: ' . $e->getMessage());
        $message = 'Hệ thống đang bận, hãy thử lại sau.';
    }
}
web_header('Đăng ký BattleBall', 'register');
?>
<main>
  <h1>Đăng ký BattleBall</h1>
  <p class="muted">Không cần email. Mỗi tài khoản có sẵn 3 nhân vật (Trái Đất, Namếc, Xayda).</p>
  <?php if ($message !== null): ?>
    <div class="msg <?= $ok ? 'ok' : 'err' ?>"><?= h($message) ?></div>
  <?php endif; ?>
  <?php if (!$ok): ?>
  <form method="post" action="register.php" autocomplete="off">
    <input type="hidden" name="_csrf" value="<?= h(csrf_token()) ?>">
    <label>Tên tài khoản <small>4 đến 20 ký tự: a-z, 0-9, _</small>
      <input type="text" name="username" value="<?= h($username) ?>" maxlength="20" required autocapitalize="none" spellcheck="false">
    </label>
    <label>Mật khẩu <small>từ 8 ký tự</small>
      <input type="password" name="password" minlength="8" maxlength="64" required autocomplete="new-password">
    </label>
    <label>Nhập lại mật khẩu
      <input type="password" name="password2" minlength="8" maxlength="64" required autocomplete="new-password">
    </label>
    <label>Mã xác nhận
      <span class="captcha"><img src="captcha.php?t=<?= time() ?>" alt="Mã xác nhận" width="190" height="56"><input type="text" name="captcha" maxlength="8" required autocapitalize="characters" autocomplete="off"></span>
    </label>
    <div class="hp" aria-hidden="true"><label>Website <input type="text" name="website" tabindex="-1" autocomplete="off"></label></div>
    <button type="submit">Tạo tài khoản</button>
  </form>
  <?php else: ?>
  <p><a class="btn ghost" href="index.php#tai-game">Tải game</a></p>
  <?php endif; ?>
</main>
<?php web_footer();
