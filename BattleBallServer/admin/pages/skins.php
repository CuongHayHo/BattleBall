<?php
// BattleBall admin: skins.
declare(strict_types=1);

function page_skins(PDO $pdo): void
{
    $kinds = lookup($pdo, 'SELECT kind_key, name_vi FROM skin_kind ORDER BY sort_order');
    $kind = (string)($_GET['kind'] ?? '');
    $q = trim((string)($_GET['q'] ?? ''));
    $where = [];
    $args = [];
    if ($kind !== '' && isset($kinds[$kind])) {
        $where[] = 't.kind_key = ?';
        $args[] = $kind;
    }
    if ($q !== '') {
        $where[] = '(t.name LIKE ? OR t.costume_id = ?)';
        $args[] = '%' . $q . '%';
        $args[] = ctype_digit($q) ? (int)$q : -1;
    }
    $st = $pdo->prepare('SELECT t.costume_id, t.name, t.kind_key, t.rarity_key, t.gem_price, t.fusion_race, t.enabled, t.gender_name,'
        . ' (SELECT COUNT(*) FROM skin_stat s WHERE s.costume_id = t.costume_id) AS n_stats'
        . ' FROM skin_template t' . ($where ? ' WHERE ' . implode(' AND ', $where) : '') . ' ORDER BY t.kind_key, t.costume_id');
    $st->execute($args);
    $rows = $st->fetchAll();
    layout_start('Skin', 'skins');
    echo '<h1>Skin</h1><form method="get" class="filter"><input type="hidden" name="p" value="skins"><select name="kind"><option value="">Mọi loại</option>';
    foreach ($kinds as $k => $lab) {
        echo '<option value="' . h($k) . '"' . ($k === $kind ? ' selected' : '') . '>' . h($lab) . '</option>';
    }
    echo '</select><input type="text" name="q" value="' . h($q) . '" placeholder="Tên hoặc mã skin"><button type="submit">Lọc</button> <span class="muted">' . count($rows) . ' skin</span></form>';
    echo '<div class="scroll"><table><thead><tr><th>Mã</th><th>Tên</th><th>Chủng tộc</th><th>Loại</th><th>Độ hiếm</th><th>Giá</th><th>Hành tinh hợp thể</th><th>Chỉ số</th><th>Bật</th><th></th></tr></thead><tbody>';
    foreach ($rows as $r) {
        echo '<tr><td>' . h($r['costume_id']) . '</td><td>' . h($r['name']) . '</td><td>' . h($r['gender_name']) . '</td><td>' . h($r['kind_key']) . '</td><td>' . h($r['rarity_key'])
            . '</td><td>' . h($r['gem_price']) . '</td><td>' . h($r['fusion_race']) . '</td><td>' . h($r['n_stats']) . '</td><td>' . ($r['enabled'] ? 'có' : '<span class="tag off">tắt</span>')
            . '</td><td><a href="?p=skin&amp;id=' . h($r['costume_id']) . '">Sửa</a></td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

function page_skin(PDO $pdo): void
{
    $id = (int)($_GET['id'] ?? 0);
    $st = $pdo->prepare('SELECT * FROM skin_template WHERE costume_id = ?');
    $st->execute([$id]);
    $skin = $st->fetch();
    if (!$skin) {
        flash('Không tìm thấy skin.', 'err');
        redirect('?p=skins');
    }
    $kinds = [];
    foreach ($pdo->query('SELECT * FROM skin_kind ORDER BY sort_order') as $k) {
        $kinds[$k['kind_key']] = $k;
    }
    $rarities = lookup($pdo, 'SELECT rarity_key, name_vi FROM skin_rarity ORDER BY sort_order');

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $ok = in_tx($pdo, function () use ($pdo, $id, $skin, $kinds, $rarities) {
            $kindKey = (string)($_POST['kind_key'] ?? '');
            if (!isset($kinds[$kindKey])) {
                throw new RuntimeException('Loại skin không hợp lệ.');
            }
            $kind = $kinds[$kindKey];
            $rarity = (string)($_POST['rarity_key'] ?? '');
            $priceRaw = trim((string)($_POST['gem_price'] ?? ''));
            $fusion = (string)($_POST['fusion_race'] ?? '');
            $new = ['kind_key' => $kindKey, 'enabled' => isset($_POST['enabled']) ? 1 : 0];
            if ($kind['sellable']) {
                if (!isset($rarities[$rarity])) {
                    throw new RuntimeException('Skin bán được phải có độ hiếm.');
                }
                if ($priceRaw === '' || filter_var($priceRaw, FILTER_VALIDATE_INT) === false || (int)$priceRaw < 0) {
                    throw new RuntimeException('Skin bán được phải có giá đá quý (số nguyên không âm).');
                }
                $new['rarity_key'] = $rarity;
                $new['gem_price'] = (int)$priceRaw;
                $new['rarity_status'] = ($rarity !== (string)$skin['rarity_key'] || (int)$priceRaw !== (int)$skin['gem_price']) ? 'MANUAL' : $skin['rarity_status'];
            } else {
                $new['rarity_key'] = null;
                $new['gem_price'] = null;
                $new['rarity_status'] = null;
            }
            if ($kind['requires_fusion_main']) {
                if (!in_array($fusion, ['td', 'xd', 'nm', 'ANY'], true)) {
                    throw new RuntimeException('Skin hợp thể phải chọn hành tinh (td, xd, nm hoặc ANY).');
                }
                $new['fusion_race'] = $fusion;
                $new['ai_can_wear'] = 0;   // AI and the fused secondary never wear fusion skins
            } else {
                $new['fusion_race'] = null;
                $new['ai_can_wear'] = isset($_POST['ai_can_wear']) ? 1 : 0;
            }
            $before = [];
            $after = [];
            foreach ($new as $col => $val) {
                if (!same_value($skin[$col], $val, in_array($col, ['enabled', 'gem_price', 'ai_can_wear'], true) ? 'int' : 'text')) {
                    $before[$col] = $skin[$col];
                    $after[$col] = $val;
                }
            }
            if ($after) {
                $set = implode(', ', array_map(fn($c) => "`$c` = ?", array_keys($after)));
                $pdo->prepare("UPDATE skin_template SET $set, version = version + 1, updated_by = ? WHERE costume_id = ?")
                    ->execute(array_merge(array_values($after), [admin_name(), $id]));
                audit($pdo, 'skin_template', (string)$id, 'UPDATE', $before, $after);
            }
            $n = save_stat_rows($pdo, 'skin_stat', 'costume_id', $id, $_POST);
            if ($after || $n) {
                bump($pdo, 'SKIN');
            }
            flash($after || $n ? 'Đã lưu skin ' . $id . '.' : 'Không có thay đổi.');
        });
        redirect($ok ? '?p=skin&id=' . $id : '?p=skin&id=' . $id);
    }

    layout_start('Skin ' . $id, 'skins');
    echo '<p><a href="?p=skins">&larr; Danh sách skin</a></p><h1>' . h($skin['name']) . ' <span class="muted">#' . h($id) . '</span></h1>';
    echo '<p class="muted">' . h($skin['gender_name']) . ' · nhóm ' . h($skin['body_group']) . ' · icon ' . h($skin['icon_id']) . ' · phiên bản ' . h($skin['version']) . ' · NRO: ' . h($skin['nro_description']) . '</p>';
    echo '<form method="post" action="?p=skin&amp;id=' . h($id) . '">' . csrf_field() . '<table><tbody>';
    $ko = '<select name="kind_key">';
    foreach ($kinds as $k => $row) {
        $ko .= '<option value="' . h($k) . '"' . ($k === $skin['kind_key'] ? ' selected' : '') . '>' . h($row['name_vi'] . ' (' . $k . ')') . '</option>';
    }
    echo field_row('Loại', $ko . '</select>', 'Loại quyết định có bán, độ hiếm, hợp thể hay không.');
    $ro = '<select name="rarity_key"><option value="">—</option>';
    foreach ($rarities as $k => $lab) {
        $ro .= '<option value="' . h($k) . '"' . ($k === $skin['rarity_key'] ? ' selected' : '') . '>' . h($lab) . '</option>';
    }
    echo field_row('Độ hiếm', $ro . '</select>', 'Trạng thái: ' . ($skin['rarity_status'] ?? 'không có') . ' (HEURISTIC = công cụ đề xuất, MANUAL = đã chỉnh tay)');
    echo field_row('Giá (đá quý)', '<input class="num" type="text" name="gem_price" value="' . h($skin['gem_price']) . '">', 'Chỉ dùng cho loại bán được.');
    $fo = '<select name="fusion_race"><option value="">—</option>';
    foreach (['td' => 'td (Trái Đất)', 'nm' => 'nm (Namếc)', 'xd' => 'xd (Xayda)', 'ANY' => 'ANY (mọi hành tinh)'] as $k => $lab) {
        $fo .= '<option value="' . $k . '"' . ($k === $skin['fusion_race'] ? ' selected' : '') . '>' . h($lab) . '</option>';
    }
    echo field_row('Hành tinh (skin hợp thể)', $fo . '</select>', 'Nhân vật chủ thuộc hành tinh này mới mặc được khi hợp thể. Chỉ áp dụng cho loại hợp thể.');
    echo field_row('AI được mặc', '<input type="checkbox" name="ai_can_wear" value="1"' . ($skin['ai_can_wear'] ? ' checked' : '') . '>', 'Luôn tắt với skin hợp thể.');
    echo field_row('Bật', '<input type="checkbox" name="enabled" value="1"' . ($skin['enabled'] ? ' checked' : '') . '>');
    echo '</tbody></table>';
    stat_rows_form($pdo, 'skin_stat', 'costume_id', $id);
    $nro = $pdo->prepare('SELECT n.stat_key, n.nro_value FROM skin_nro_stat n WHERE n.costume_id = ?');
    $nro->execute([$id]);
    $list = array_map(fn($r) => $r['stat_key'] . ' ' . $r['nro_value'], $nro->fetchAll());
    if ($list) {
        echo '<p class="muted">Chỉ số gốc trong NRO (tham khảo): ' . h(implode(', ', $list)) . '</p>';
    }
    echo '<p><button type="submit">Lưu</button></p></form>';
    layout_end();
}

register_page('skins', 'Skin', 'skin', 'page_skins', 5);
register_hidden('skin', 'page_skin', 'skins');
