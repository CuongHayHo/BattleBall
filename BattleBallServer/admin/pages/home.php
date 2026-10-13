<?php
// BattleBall admin: overview. Number cards, a shortcut card per menu group and the version of every static data domain.
declare(strict_types=1);

function page_home(PDO $pdo): void
{
    layout_start('Tổng quan', 'home');
    echo '<h1>Tổng quan</h1>';
    $counts = [
        'Tài khoản' => 'SELECT COUNT(*) FROM account', 'Nhân vật' => 'SELECT COUNT(*) FROM game_character',
        'Skin' => 'SELECT COUNT(*) FROM skin_template', 'Skin đang bật' => 'SELECT COUNT(*) FROM skin_template WHERE enabled = 1',
        'Đồ hỗ trợ' => 'SELECT COUNT(*) FROM support_item_template',
        'Loại chỉ số đang bật' => 'SELECT COUNT(*) FROM stat_type WHERE enabled = 1', 'Loại chỉ số đang tắt' => 'SELECT COUNT(*) FROM stat_type WHERE enabled = 0',
    ];
    echo '<div class="stats">';
    foreach ($counts as $lab => $sql) {
        echo '<div class="stat"><span>' . h($lab) . '</span><b>' . h(number_format((int)$pdo->query($sql)->fetchColumn())) . '</b></div>';
    }
    echo '</div><h2>Đi tới</h2><div class="shortcuts">';
    foreach (menu_tree() as $g => $grp) {
        if ($grp['title'] === '') {
            continue;
        }
        echo '<section class="shortcut"><h3>' . h($grp['title']) . '</h3>';
        foreach ($grp['items'] as $it) {
            echo '<a href="' . menu_href($it) . '">' . h($it['label']) . '</a>';
        }
        echo '</section>';
    }
    echo '</div><h2>Phiên bản dữ liệu</h2><p class="muted">Game server nạp lại dữ liệu khi phiên bản của nhóm đổi (kiểm tra mỗi 30 giây).</p>'
        . '<div class="scroll"><table><thead><tr><th>Nhóm</th><th>Phiên bản</th><th>Cập nhật</th><th>Bởi</th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT domain, version, updated_at, updated_by FROM static_data_version ORDER BY domain') as $r) {
        echo '<tr><td>' . h($r['domain']) . '</td><td>' . h($r['version']) . '</td><td>' . h($r['updated_at']) . '</td><td>' . h($r['updated_by']) . '</td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

register_page('home', 'Tổng quan', 'home', 'page_home', 0);
