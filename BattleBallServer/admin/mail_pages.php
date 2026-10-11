<?php
// Admin pages for the mailbox (gifts to players) and the notices. Tables: db/migrations/V027__mailbox_and_notices.sql.
// A mail is one row per account: "send to everyone" writes one row for each existing player account (each player takes their own gift).
declare(strict_types=1);

const MAIL_KINDS = [
    'GOLD' => 'Vàng (số lượng)',
    'GEMS' => 'Đá quý (số lượng)',
    'SKIN' => 'Skin / cải trang (mã costume)',
    'MATERIAL' => 'Nguyên liệu (mã nguyên liệu; mảnh bông tai = 1820)',
    'ITEM' => 'Đồ trang bị (mã mẫu đồ; số lượng = số món)',
    'EARRING' => 'Bông tai Porata (một cái mỗi tài khoản)',
];
const MAIL_MAX_LINES = 5;

function mail_kind_label(string $kind): string
{
    return MAIL_KINDS[$kind] ?? $kind;
}

/** Checks one attachment line from the form; returns [kind, ref, quantity] or throws with a message for the admin. */
function mail_check_line(PDO $pdo, string $kind, string $refRaw, string $qtyRaw): array
{
    if (!isset(MAIL_KINDS[$kind])) {
        throw new RuntimeException('Loại quà không hợp lệ.');
    }
    $qty = (int)$qtyRaw;
    $ref = (int)$refRaw;
    switch ($kind) {
        case 'GOLD':
        case 'GEMS':
            if ($qty < 1 || $qty > 1000000000) {
                throw new RuntimeException('Số lượng ' . ($kind === 'GOLD' ? 'vàng' : 'đá quý') . ' phải từ 1 đến 1.000.000.000.');
            }
            return [$kind, 0, $qty];
        case 'EARRING':
            return [$kind, 0, 1];
        case 'SKIN':
            $found = $pdo->prepare('SELECT COUNT(*) FROM skin_template WHERE costume_id = ?');
            $found->execute([$ref]);
            if (!(int)$found->fetchColumn()) {
                throw new RuntimeException('Không có skin mã ' . $ref . '.');
            }
            return [$kind, $ref, 1];
        case 'MATERIAL':
        case 'ITEM':
            $table = $kind === 'MATERIAL' ? 'material_template' : 'item_template';
            $found = $pdo->prepare("SELECT COUNT(*) FROM $table WHERE template_id = ?");
            $found->execute([$ref]);
            if (!(int)$found->fetchColumn()) {
                throw new RuntimeException('Không có ' . ($kind === 'MATERIAL' ? 'nguyên liệu' : 'đồ trang bị') . ' mã ' . $ref . '.');
            }
            if ($qty < 1 || $qty > ($kind === 'ITEM' ? 200 : 100000)) {
                throw new RuntimeException('Số lượng phải từ 1 đến ' . ($kind === 'ITEM' ? '200' : '100.000') . '.');
            }
            return [$kind, $ref, $qty];
    }
    throw new RuntimeException('Loại quà không hợp lệ.');
}

function mail_gift_summary(PDO $pdo, int $mailId): string
{
    $st = $pdo->prepare('SELECT kind, ref_id, quantity FROM mail_attachment WHERE mail_id = ? ORDER BY line_no');
    $st->execute([$mailId]);
    $parts = [];
    foreach ($st as $a) {
        $parts[] = match ($a['kind']) {
            'GOLD' => number_format((float)$a['quantity'], 0, ',', '.') . ' vàng',
            'GEMS' => number_format((float)$a['quantity'], 0, ',', '.') . ' đá quý',
            'SKIN' => 'skin #' . $a['ref_id'],
            'MATERIAL' => 'nguyên liệu #' . $a['ref_id'] . ' x' . $a['quantity'],
            'ITEM' => 'đồ #' . $a['ref_id'] . ' x' . $a['quantity'],
            'EARRING' => 'bông tai Porata',
            default => $a['kind'],
        };
    }
    return implode(', ', $parts);
}

function page_mail(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $target = trim((string)($_POST['target'] ?? ''));
        $title = trim((string)($_POST['title'] ?? ''));
        $body = trim((string)($_POST['body'] ?? ''));
        $lines = [];
        $ok = in_tx($pdo, function () use ($pdo, $target, $title, $body, &$lines, &$sent) {
            if ($title === '' || mb_strlen($title) > 80) {
                throw new RuntimeException('Tiêu đề không được trống và tối đa 80 ký tự.');
            }
            if (mb_strlen($body) > 1000) {
                throw new RuntimeException('Nội dung tối đa 1000 ký tự.');
            }
            for ($i = 0; $i < MAIL_MAX_LINES; $i++) {
                $kind = (string)($_POST['kind'][$i] ?? '');
                if ($kind === '') {
                    continue;
                }
                $lines[] = mail_check_line($pdo, $kind, (string)($_POST['ref'][$i] ?? ''), (string)($_POST['qty'][$i] ?? ''));
            }
            if ($target === '') {
                throw new RuntimeException('Chọn người nhận: nhập tên tài khoản, hoặc ghi * để gửi tất cả.');
            }
            if ($target === '*') {
                $ids = $pdo->query("SELECT account_id FROM account WHERE role = 'PLAYER' AND banned = 0")->fetchAll(PDO::FETCH_COLUMN);
            } else {
                $st = $pdo->prepare('SELECT account_id FROM account WHERE username = ?');
                $st->execute([$target]);
                $ids = $st->fetchAll(PDO::FETCH_COLUMN);
                if (!$ids) {
                    throw new RuntimeException('Không có tài khoản tên "' . $target . '".');
                }
            }
            $insMail = $pdo->prepare('INSERT INTO mail (account_id, title, body, sent_by) VALUES (?,?,?,?)');
            $insLine = $pdo->prepare('INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) VALUES (?,?,?,?,?)');
            foreach ($ids as $accountId) {
                $insMail->execute([$accountId, $title, $body, admin_name()]);
                $mailId = (int)$pdo->lastInsertId();
                foreach ($lines as $n => [$kind, $ref, $qty]) {
                    $insLine->execute([$mailId, $n + 1, $kind, $ref, $qty]);
                }
            }
            $sent = count($ids);
            audit($pdo, 'mail', $target, 'INSERT', null, ['title' => $title, 'recipients' => $sent, 'gifts' => $lines]);
        });
        if ($ok) {
            flash('Đã gửi thư "' . $title . '" tới ' . $sent . ' tài khoản.');
        }
        redirect('?p=mail');
    }

    layout_start('Hòm thư', 'mail');
    echo '<h1>Hòm thư: gửi quà cho người chơi</h1><p class="muted">Người chơi mở Chức năng → Hòm thư và bấm Nhận. Quà vào kho chung của tài khoản (vàng, đá quý, skin, nguyên liệu, đồ trang bị). '
        . 'Mỗi thư là một bản riêng cho từng tài khoản; khi gửi tất cả, tài khoản tạo sau đó sẽ không nhận.</p>';
    echo '<form method="post" action="?p=mail" class="card">' . csrf_field()
        . '<label>Gửi tới (tên tài khoản, hoặc * là tất cả)<input type="text" name="target" placeholder="tdno01" required></label>'
        . '<label>Tiêu đề<input type="text" name="title" maxlength="80" required></label>'
        . '<label>Nội dung<textarea name="body" rows="4" maxlength="1000"></textarea></label>'
        . '<table><thead><tr><th>Quà kèm (tối đa ' . MAIL_MAX_LINES . ')</th><th>Mã</th><th>Số lượng</th></tr></thead><tbody>';
    for ($i = 0; $i < MAIL_MAX_LINES; $i++) {
        echo '<tr><td><select name="kind[' . $i . ']"><option value="">(không)</option>';
        foreach (MAIL_KINDS as $k => $lab) {
            echo '<option value="' . h($k) . '">' . h($lab) . '</option>';
        }
        echo '</select></td><td><input class="num" type="text" name="ref[' . $i . ']"></td><td><input class="num" type="text" name="qty[' . $i . ']" value="1"></td></tr>';
    }
    echo '</tbody></table><p><button type="submit">Gửi thư</button></p></form>';

    echo '<h2>Mã tham khảo</h2><div class="scroll"><table><thead><tr><th>Nguyên liệu: mã</th><th>Tên</th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT template_id, name_vi FROM material_template WHERE enabled = 1 ORDER BY template_id') as $r) {
        echo '<tr><td>' . h($r['template_id']) . '</td><td>' . h($r['name_vi']) . '</td></tr>';
    }
    echo '</tbody></table></div><div class="scroll"><table><thead><tr><th>Đồ trang bị: mã</th><th>Tên</th><th>Ô</th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT template_id, name_vi, slot FROM item_template WHERE enabled = 1 ORDER BY template_id') as $r) {
        echo '<tr><td>' . h($r['template_id']) . '</td><td>' . h($r['name_vi']) . '</td><td>' . h($r['slot']) . '</td></tr>';
    }
    echo '</tbody></table></div><p class="muted">Mã skin: xem trang Skin (cột mã costume).</p>';

    $filter = trim((string)($_GET['user'] ?? ''));
    echo '<h2>Thư đã gửi gần đây</h2><form method="get" class="filter"><input type="hidden" name="p" value="mail"><input type="text" name="user" value="' . h($filter)
        . '" placeholder="lọc theo tên tài khoản"><button type="submit">Lọc</button></form>';
    $sql = 'SELECT m.mail_id, a.username, m.title, m.sent_at, m.read_at, m.claimed_at FROM mail m JOIN account a USING (account_id)'
        . ($filter !== '' ? ' WHERE a.username = ?' : '') . ' ORDER BY m.mail_id DESC LIMIT 100';
    $st = $pdo->prepare($sql);
    $st->execute($filter !== '' ? [$filter] : []);
    echo '<div class="scroll"><table><thead><tr><th>Mã thư</th><th>Tài khoản</th><th>Tiêu đề</th><th>Quà</th><th>Gửi lúc</th><th>Đã đọc</th><th>Đã nhận quà</th></tr></thead><tbody>';
    foreach ($st as $r) {
        echo '<tr><td>' . h($r['mail_id']) . '</td><td>' . h($r['username']) . '</td><td>' . h($r['title']) . '</td><td>' . h(mail_gift_summary($pdo, (int)$r['mail_id']))
            . '</td><td>' . h($r['sent_at']) . '</td><td>' . ($r['read_at'] ? 'rồi' : '') . '</td><td>' . ($r['claimed_at'] ? 'rồi' : '') . '</td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

function page_notices(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $action = (string)($_POST['action'] ?? '');
        $id = (int)($_POST['id'] ?? 0);
        $ok = in_tx($pdo, function () use ($pdo, $action, $id) {
            if ($action === 'add') {
                $title = trim((string)($_POST['title'] ?? ''));
                $body = trim((string)($_POST['body'] ?? ''));
                $expires = trim((string)($_POST['expires_at'] ?? ''));
                if ($title === '' || mb_strlen($title) > 80 || mb_strlen($body) > 2000) {
                    throw new RuntimeException('Tiêu đề không được trống (tối đa 80 ký tự), nội dung tối đa 2000 ký tự.');
                }
                if ($expires !== '' && strtotime($expires) === false) {
                    throw new RuntimeException('Ngày hết hạn không hợp lệ (ví dụ 2026-12-31 hoặc để trống).');
                }
                $pdo->prepare('INSERT INTO notice (title, body, expires_at, created_by) VALUES (?,?,?,?)')
                    ->execute([$title, $body, $expires === '' ? null : date('Y-m-d H:i:s', strtotime($expires)), admin_name()]);
                audit($pdo, 'notice', (string)$pdo->lastInsertId(), 'INSERT', null, ['title' => $title]);
            } elseif ($action === 'toggle') {
                $pdo->prepare('UPDATE notice SET enabled = 1 - enabled WHERE notice_id = ?')->execute([$id]);
                audit($pdo, 'notice', (string)$id, 'UPDATE', null, ['toggled' => true]);
            } elseif ($action === 'delete') {
                $pdo->prepare('DELETE FROM notice WHERE notice_id = ?')->execute([$id]);
                audit($pdo, 'notice', (string)$id, 'DELETE', null, null);
            } else {
                throw new RuntimeException('Hành động không hợp lệ.');
            }
        });
        if ($ok) {
            flash('Đã lưu.');
        }
        redirect('?p=notices');
    }

    layout_start('Thông báo', 'notices');
    echo '<h1>Thông báo cho người chơi</h1><p class="muted">Hiện ở Chức năng → Thông báo (mới nhất trên cùng). Thông báo tắt hoặc quá hạn không hiện.</p>'
        . '<form method="post" action="?p=notices" class="card">' . csrf_field() . '<input type="hidden" name="action" value="add">'
        . '<label>Tiêu đề<input type="text" name="title" maxlength="80" required></label>'
        . '<label>Nội dung<textarea name="body" rows="5" maxlength="2000"></textarea></label>'
        . '<label>Hết hạn (tùy chọn, ví dụ 2026-12-31)<input type="text" name="expires_at"></label>'
        . '<button type="submit">Đăng thông báo</button></form>'
        . '<h2>Đã đăng</h2><div class="scroll"><table><thead><tr><th>Mã</th><th>Tiêu đề</th><th>Đăng lúc</th><th>Hết hạn</th><th>Bật</th><th></th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT notice_id, title, created_at, expires_at, enabled FROM notice ORDER BY notice_id DESC LIMIT 100') as $r) {
        echo '<tr><td>' . h($r['notice_id']) . '</td><td>' . h($r['title']) . '</td><td>' . h($r['created_at']) . '</td><td>' . h($r['expires_at']) . '</td><td>'
            . ($r['enabled'] ? 'có' : '<span class="tag off">tắt</span>') . '</td><td>'
            . '<form method="post" action="?p=notices" class="inline">' . csrf_field() . '<input type="hidden" name="id" value="' . h($r['notice_id']) . '">'
            . '<button type="submit" name="action" value="toggle" class="link">Bật/tắt</button> '
            . '<button type="submit" name="action" value="delete" class="link" onclick="return confirm(\'Xóa thông báo này?\')">Xóa</button></form></td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

const WELCOME_MAX_LINES = 10;

/** The welcome mail every new account gets (V047): the switch, the text and the gift lines. Turning it off only stops FUTURE accounts; mails already sent stay. */
function page_welcome(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $enabled = isset($_POST['enabled']) ? 1 : 0;
        $title = trim((string)($_POST['title'] ?? ''));
        $body = trim((string)($_POST['body'] ?? ''));
        $lines = [];
        $ok = in_tx($pdo, function () use ($pdo, $enabled, $title, $body, &$lines) {
            if ($title === '' || mb_strlen($title) > 80) {
                throw new RuntimeException('Tiêu đề không được trống và tối đa 80 ký tự.');
            }
            if (mb_strlen($body) > 1000) {
                throw new RuntimeException('Nội dung tối đa 1000 ký tự.');
            }
            for ($i = 0; $i < WELCOME_MAX_LINES; $i++) {
                $kind = (string)($_POST['kind'][$i] ?? '');
                if ($kind === '') {
                    continue;
                }
                $lines[] = mail_check_line($pdo, $kind, (string)($_POST['ref'][$i] ?? ''), (string)($_POST['qty'][$i] ?? ''));
            }
            $before = $pdo->query('SELECT enabled, title FROM welcome_mail_setting WHERE setting_id = 1')->fetch(PDO::FETCH_ASSOC) ?: null;
            $pdo->prepare('UPDATE welcome_mail_setting SET enabled = ?, title = ?, body = ?, updated_by = ? WHERE setting_id = 1')->execute([$enabled, $title, $body, admin_name()]);
            $pdo->exec('DELETE FROM welcome_gift');
            $ins = $pdo->prepare('INSERT INTO welcome_gift (line_no, kind, ref_id, quantity) VALUES (?,?,?,?)');
            foreach ($lines as $n => [$kind, $ref, $qty]) {
                $ins->execute([$n + 1, $kind, $ref, $qty]);
            }
            audit($pdo, 'welcome_mail', '1', 'UPDATE', $before, ['enabled' => $enabled, 'title' => $title, 'gifts' => $lines]);
        });
        if ($ok) {
            flash($enabled ? 'Đã lưu. Tài khoản mới sẽ nhận thư quà tân thủ.' : 'Đã lưu. Quà tân thủ đang TẮT: tài khoản mới không nhận thư.');
        }
        redirect('?p=welcome');
    }

    $set = $pdo->query('SELECT enabled, title, body, updated_at, updated_by FROM welcome_mail_setting WHERE setting_id = 1')->fetch(PDO::FETCH_ASSOC);
    if (!$set) {
        $set = ['enabled' => 0, 'title' => 'Quà tân thủ', 'body' => '', 'updated_at' => '', 'updated_by' => ''];
    }
    $gifts = $pdo->query('SELECT kind, ref_id, quantity FROM welcome_gift ORDER BY line_no')->fetchAll(PDO::FETCH_ASSOC);
    $sentTotal = (int)$pdo->query("SELECT COUNT(*) FROM mail WHERE sent_by = 'system'")->fetchColumn();

    layout_start('Quà tân thủ', 'welcome');
    echo '<h1>Quà tân thủ: thư gửi tự động cho tài khoản mới</h1><p class="muted">Mỗi tài khoản mới (đăng ký trong game hoặc trên web) nhận ngay một thư này trong Hòm thư. '
        . 'Tắt công tắc thì tài khoản tạo từ lúc đó không nhận nữa; thư đã gửi trước đó vẫn nằm trong hòm thư của người chơi. Đã gửi tổng cộng: ' . $sentTotal . ' thư.</p>';
    echo '<form method="post" action="?p=welcome" class="card">' . csrf_field()
        . '<label><input type="checkbox" name="enabled" value="1"' . ($set['enabled'] ? ' checked' : '') . '> <strong>BẬT</strong> gửi quà tân thủ cho tài khoản mới</label>'
        . '<label>Tiêu đề<input type="text" name="title" maxlength="80" value="' . h($set['title']) . '" required></label>'
        . '<label>Nội dung<textarea name="body" rows="4" maxlength="1000">' . h($set['body']) . '</textarea></label>'
        . '<table><thead><tr><th>Quà kèm (tối đa ' . WELCOME_MAX_LINES . ')</th><th>Mã</th><th>Số lượng</th></tr></thead><tbody>';
    for ($i = 0; $i < WELCOME_MAX_LINES; $i++) {
        $g = $gifts[$i] ?? null;
        echo '<tr><td><select name="kind[' . $i . ']"><option value="">(không)</option>';
        foreach (MAIL_KINDS as $k => $lab) {
            echo '<option value="' . h($k) . '"' . ($g && $g['kind'] === $k ? ' selected' : '') . '>' . h($lab) . '</option>';
        }
        echo '</select></td><td><input class="num" type="text" name="ref[' . $i . ']" value="' . h($g ? $g['ref_id'] : '') . '"></td>'
            . '<td><input class="num" type="text" name="qty[' . $i . ']" value="' . h($g ? $g['quantity'] : '1') . '"></td></tr>';
    }
    echo '</tbody></table><button type="submit">Lưu</button></form>';
    echo '<p class="muted">Mã nguyên liệu: 201 Đá nâng cấp, 202 Đá chỉ số, 203 Đá pha lê, 204 Đá GEN, 205 Đá mở ô GEN. Lần lưu cuối: ' . h($set['updated_at']) . ' bởi ' . h($set['updated_by']) . '.</p>';
    layout_end();
}
