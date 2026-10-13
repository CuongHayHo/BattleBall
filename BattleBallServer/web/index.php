<?php
// BattleBall web: home page. Static introduction, a button to the registration page and the download link (config download_url / BB_DOWNLOAD_URL).
declare(strict_types=1);
require __DIR__ . '/lib.php';
require __DIR__ . '/layout.php';
send_security_headers();

$download = trim((string)cfg('download_url', ''));
if (!preg_match('~^https?://~i', $download)) {
    $download = '';     // only a web link is accepted
}

web_header('BattleBall', 'home');
?>
<main class="wide">
  <section class="hero">
    <p class="eyebrow">Game hành động nhập vai trên Android</p>
    <div class="logo"><span class="runner" role="img" aria-label="Nhân vật chạy bộ"></span><h1 aria-label="BattleBall"><span class="ch">B</span><span class="ch">a</span><span class="ch">t</span><span class="ch">t</span><span class="ch">l</span><span class="ch">e</span><span class="ch b">B</span><span class="ch b">a</span><span class="ch b">l</span><span class="ch b">l</span></h1></div>
    <p class="lead">Ba nhân vật của ba hành tinh trong một tài khoản. Luyện kỹ năng, mặc skin, vào phó bản đánh boss.</p>
    <div class="actions">
      <a class="btn" href="login.php">Đăng nhập</a>
      <a class="btn ghost" href="register.php">Tạo tài khoản</a>
      <?php if ($download !== ''): ?>
        <a class="btn ghost" href="<?= h($download) ?>">Tải game</a>
      <?php else: ?>
        <a class="btn ghost" href="#tai-game">Cách tải game</a>
      <?php endif; ?>
    </div>
  </section>

  <section class="cards">
    <article class="feature"><h2>Ba hành tinh</h2><p>Trái Đất, Namếc, Xayda: mỗi tài khoản có sẵn ba nhân vật dùng chung một chiếc túi.</p></article>
    <article class="feature"><h2>13 phó bản</h2><p>Mỗi phó bản có ba độ khó: Dễ, Khó, Địa ngục. Năm đợt quái nhỏ rồi đến boss.</p></article>
    <article class="feature"><h2>Skin và Aura</h2><p>Hơn 200 skin bán trong làng, 65 Aura mở khóa theo cấp, trang bị nâng cấp và dòng GEN.</p></article>
    <article class="feature"><h2>Nhiệm vụ mỗi ngày</h2><p>Điểm danh, chuỗi nhiệm vụ và nhiệm vụ ngày trả vàng, ngọc, đá và thẻ kỹ năng.</p></article>
  </section>

  <section class="steps" id="tai-game">
    <h2>Bắt đầu trong 3 bước</h2>
    <ol>
      <li><b>Đăng ký</b><span>Chọn tên tài khoản và mật khẩu ngay trên <a href="register.php">trang đăng ký</a>. Không cần email.</span></li>
      <li><b>Tải game</b><span><?php if ($download !== ''): ?>Tải bản mới nhất <a href="<?= h($download) ?>">tại đây</a>.<?php else: ?>Đường dẫn tải game sẽ có ở đây khi bản phát hành sẵn sàng.<?php endif; ?></span></li>
      <li><b>Đăng nhập</b><span>Mở game, nhập tài khoản vừa tạo và vào làng.</span></li>
    </ol>
  </section>
</main>
<?php web_footer();
