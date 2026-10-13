<?php
// BattleBall web: the page frame shared by the home page and the registration page (top bar, content, footer).
// No inline style or script: the Content-Security-Policy (lib.php) allows only style.css from this site.
declare(strict_types=1);

function web_header(string $title, string $active = ''): void
{
    $link = fn(string $href, string $label, string $key) => '<a href="' . $href . '"' . ($key === $active ? ' class="on"' : '') . '>' . h($label) . '</a>';
    echo '<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        . '<title>' . h($title) . '</title><link rel="stylesheet" href="style.css"></head><body>'
        . '<header class="site"><a class="brand" href="index.php"><span class="runner sm" role="img" aria-label="Nhân vật chạy bộ"></span><span>Battle<b>Ball</b></span></a><nav>'
        . $link('index.php', 'Trang chủ', 'home') . $link('login.php', 'Đăng nhập', 'login') . $link('register.php', 'Đăng ký', 'register') . $link('index.php#tai-game', 'Tải game', 'download')
        . '</nav></header>' . web_ticker();
}

/** The running line under the top bar: the same sentences twice in a row so the loop has no gap (the second copy is hidden from screen readers). */
function web_ticker(): string
{
    $items = [
        'Mỗi tài khoản có sẵn 3 nhân vật: Trái Đất, Namếc, Xayda',
        '13 phó bản, 3 độ khó: Dễ, Khó, Địa ngục',
        'Hơn 200 skin và 65 Aura mở khóa theo cấp',
        'Điểm danh và nhiệm vụ mỗi ngày nhận vàng, ngọc, đá, thẻ kỹ năng',
        'Đăng ký không cần email',
    ];
    $copy = fn(bool $hidden) => '<span class="copy"' . ($hidden ? ' aria-hidden="true"' : '') . '>'
        . implode('', array_map(fn($t) => '<span class="item">' . h($t) . '</span>', $items)) . '</span>';
    return '<div class="ticker" role="marquee" aria-label="Thông báo"><div class="track">' . $copy(false) . $copy(true) . '</div></div>';
}

function web_footer(): void
{
    echo '<footer class="site">BattleBall · Mỗi tài khoản có sẵn 3 nhân vật · Không cần email</footer></body></html>';
}
