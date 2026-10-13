<?php
// BattleBall admin: edit log.
declare(strict_types=1);

function page_audit(PDO $pdo): void
{
    layout_start('Nhật ký', 'audit');
    echo '<h1>Nhật ký chỉnh sửa</h1><p class="muted">200 thay đổi gần nhất.</p><div class="scroll"><table><thead><tr><th>Thời điểm</th><th>Người sửa</th><th>Bảng</th><th>Khóa</th><th>Hành động</th><th>Trước</th><th>Sau</th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT created_at, admin_user, table_name, row_key, action, before_json, after_json FROM admin_audit_log ORDER BY id DESC LIMIT 200') as $r) {
        echo '<tr><td>' . h($r['created_at']) . '</td><td>' . h($r['admin_user']) . '</td><td>' . h($r['table_name']) . '</td><td>' . h($r['row_key']) . '</td><td>' . h($r['action'])
            . '</td><td class="code">' . h($r['before_json']) . '</td><td class="code">' . h($r['after_json']) . '</td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

register_page('audit', 'Nhật ký', 'system', 'page_audit', 100);
