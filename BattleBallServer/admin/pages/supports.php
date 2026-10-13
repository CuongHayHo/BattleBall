<?php
// BattleBall admin: support items (pet, training armor, flag/wing/weapon, skill book).
declare(strict_types=1);

/** Kind-specific table and columns of a support item (pet effect, training armor, skill book); [] when the kind has none. */
function support_extra_spec(string $kind, array $stats): array
{
    if ($kind === 'PET') {
        return ['table' => 'support_pet_effect', 'cols' => [
            ['name' => 'stat_key', 'label' => 'Chỉ số buff', 'type' => 'select', 'options' => $stats], ['name' => 'value', 'label' => 'Giá trị', 'type' => 'num'],
            ['name' => 'interval_s', 'label' => 'Chu kỳ (giây)', 'type' => 'int'], ['name' => 'duration_s', 'label' => 'Thời lượng (giây)', 'type' => 'int'],
        ]];
    }
    if ($kind === 'TRAIN_ARMOR') {
        return ['table' => 'support_train_armor', 'cols' => [
            ['name' => 'atk_penalty_pct', 'label' => 'Giảm sức đánh khi mặc (%)', 'type' => 'num'], ['name' => 'atk_bonus_pct', 'label' => 'Tăng sức đánh khi cởi (%)', 'type' => 'num'],
            ['name' => 'points_per_match', 'label' => 'Điểm mỗi trận', 'type' => 'int'], ['name' => 'max_points', 'label' => 'Điểm tối đa', 'type' => 'int'],
        ]];
    }
    if ($kind === 'SKILL_BOOK') {
        return ['table' => 'skill_book_template', 'cols' => [['name' => 'lines_count', 'label' => 'Số dòng chỉ số', 'type' => 'int']]];
    }
    return [];
}

function support_base_cols(): array
{
    return [
        ['name' => 'obtain', 'label' => 'Cách nhận', 'type' => 'select', 'options' => ['SHOP' => 'SHOP (mua bằng đá quý)', 'CRAFT' => 'CRAFT (ghép vật liệu)', 'DROP' => 'DROP (rơi từ boss)', 'NONE' => 'NONE (không nhận được)']],
        ['name' => 'race', 'label' => 'Giới hạn chủng tộc', 'type' => 'select', 'options' => ['ANY' => 'Mọi chủng tộc', 'EARTH' => 'Trái Đất', 'NAMEC' => 'Namếc', 'XAYDA' => 'Xayda']],
        ['name' => 'gem_price', 'label' => 'Giá đá quý', 'type' => 'int', 'null' => true],
        ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
        ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
    ];
}

function page_support_new(PDO $pdo): void
{
    $kinds = lookup($pdo, 'SELECT kind_key, name_vi FROM support_kind WHERE enabled = 1 ORDER BY slot');
    $kind = (string)($_GET['kind'] ?? '');
    if (!isset($kinds[$kind])) {
        flash('Chọn nhóm đồ hỗ trợ cần thêm.', 'warn');
        redirect('?p=supports');
    }
    $stats = lookup($pdo, 'SELECT stat_key, name_vi FROM stat_type ORDER BY sort_order');
    $idCol = ['name' => 'support_id', 'label' => 'Mã (id vật phẩm NRO hoặc số chưa dùng)', 'type' => 'int'];
    $nameCol = ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120];
    $iconCol = ['name' => 'icon_id', 'label' => 'Mã icon NRO', 'type' => 'int', 'null' => true];
    $base = support_base_cols();
    $extra = support_extra_spec($kind, $stats);

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $newId = null;
        $ok = in_tx($pdo, function () use ($pdo, $kind, $idCol, $nameCol, $iconCol, $base, $extra, &$newId) {
            $id = read_value($idCol, $_POST);
            $exists = $pdo->prepare('SELECT COUNT(*) FROM support_item_template WHERE support_id = ?');
            $exists->execute([$id]);
            if ((int)$exists->fetchColumn() > 0) {
                throw new RuntimeException('Mã này đã tồn tại.');
            }
            $vals = ['support_id' => $id, 'kind_key' => $kind, 'name_vi' => read_value($nameCol, $_POST), 'icon_id' => read_value($iconCol, $_POST)];
            foreach ($base as $c) {
                $vals[$c['name']] = read_value($c, $_POST);
            }
            if ($vals['obtain'] === 'SHOP' && $vals['gem_price'] === null) {
                throw new RuntimeException('Đồ bán trong shop phải có giá đá quý.');
            }
            $vals['updated_by'] = admin_name();
            $pdo->prepare('INSERT INTO support_item_template (' . implode(',', array_map(fn($n) => "`$n`", array_keys($vals))) . ') VALUES (' . implode(',', array_fill(0, count($vals), '?')) . ')')
                ->execute(array_values($vals));
            audit($pdo, 'support_item_template', (string)$id, 'INSERT', null, $vals);
            if ($extra) {
                $ev = ['support_id' => $id];
                foreach ($extra['cols'] as $c) {
                    $ev[$c['name']] = read_value($c, $_POST);
                }
                $ev['updated_by'] = admin_name();
                $pdo->prepare('INSERT INTO `' . $extra['table'] . '` (' . implode(',', array_map(fn($n) => "`$n`", array_keys($ev))) . ') VALUES (' . implode(',', array_fill(0, count($ev), '?')) . ')')
                    ->execute(array_values($ev));
                audit($pdo, $extra['table'], (string)$id, 'INSERT', null, $ev);
            }
            bump($pdo, 'SUPPORT');
            $newId = $id;
            flash('Đã thêm đồ hỗ trợ ' . $id . '.' . ($kind === 'FLAG_WING_WEAPON' ? ' Thêm chỉ số ở trang sửa.' : ''));
        });
        redirect($ok ? '?p=support&id=' . $newId : '?p=support_new&kind=' . urlencode($kind));
    }

    layout_start('Thêm đồ hỗ trợ', 'supports');
    echo '<p><a href="?p=supports">&larr; Danh sách đồ hỗ trợ</a></p><h1>Thêm: ' . h($kinds[$kind]) . '</h1><form method="post" action="?p=support_new&amp;kind=' . h($kind) . '">' . csrf_field() . '<table><tbody>';
    foreach (array_merge([$idCol, $nameCol, $iconCol], $base, $extra['cols'] ?? []) as $c) {
        echo field_row($c['label'], render_input($c, $c['name'], $c['name'] === 'enabled' ? '1' : ($c['name'] === 'obtain' ? 'SHOP' : ($c['name'] === 'race' ? 'ANY' : null))));
    }
    echo '</tbody></table><p><button type="submit">Thêm</button></p></form>';
    layout_end();
}

function page_supports(PDO $pdo): void
{
    layout_start('Đồ hỗ trợ', 'supports');
    echo '<h1>Đồ hỗ trợ vĩnh viễn</h1><p class="muted">Mỗi nhóm một ô: pet, giáp tập luyện, cờ/cánh/vũ khí, sách tuyệt kỹ.</p>'
        . '<div class="scroll"><table><thead><tr><th>Mã</th><th>Tên</th><th>Nhóm</th><th>Nhận</th><th>Giá</th><th>Bật</th><th></th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT t.support_id, t.name_vi, k.name_vi AS kind, t.obtain, t.gem_price, t.enabled FROM support_item_template t JOIN support_kind k USING (kind_key) ORDER BY k.slot, t.support_id') as $r) {
        echo '<tr><td>' . h($r['support_id']) . '</td><td>' . h($r['name_vi']) . '</td><td>' . h($r['kind']) . '</td><td>' . h($r['obtain']) . '</td><td>' . h($r['gem_price'])
            . '</td><td>' . ($r['enabled'] ? 'có' : '<span class="tag off">tắt</span>') . '</td><td><a href="?p=support&amp;id=' . h($r['support_id']) . '">Sửa</a></td></tr>';
    }
    echo '</tbody></table></div><h2>Thêm đồ mới</h2><p>';
    foreach ($pdo->query('SELECT kind_key, name_vi FROM support_kind WHERE enabled = 1 ORDER BY slot') as $k) {
        echo '<a href="?p=support_new&amp;kind=' . h($k['kind_key']) . '">+ ' . h($k['name_vi']) . '</a> &nbsp; ';
    }
    echo '</p>';
    layout_end();
}

function page_support(PDO $pdo): void
{
    $id = (int)($_GET['id'] ?? 0);
    $st = $pdo->prepare('SELECT * FROM support_item_template WHERE support_id = ?');
    $st->execute([$id]);
    $it = $st->fetch();
    if (!$it) {
        flash('Không tìm thấy đồ hỗ trợ.', 'err');
        redirect('?p=supports');
    }
    $kind = $it['kind_key'];
    $stats = lookup($pdo, 'SELECT stat_key, name_vi FROM stat_type ORDER BY sort_order');
    $base = support_base_cols();
    $extra = support_extra_spec($kind, $stats);

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        in_tx($pdo, function () use ($pdo, $id, $kind, $base, $extra) {
            $changed = false;
            [, $after] = update_cols($pdo, 'support_item_template', 'support_id', $id, $base, $_POST);
            $changed = $changed || (bool)$after;
            if ($after) {
                $pdo->prepare('UPDATE support_item_template SET version = version + 1 WHERE support_id = ?')->execute([$id]);
            }
            $price = $_POST['gem_price'] ?? '';
            if (($_POST['obtain'] ?? '') === 'SHOP' && trim((string)$price) === '') {
                throw new RuntimeException('Đồ bán trong shop phải có giá đá quý.');
            }
            if ($extra) {
                [, $a2] = update_cols($pdo, $extra['table'], 'support_id', $id, $extra['cols'], $_POST);
                $changed = $changed || (bool)$a2;
            }
            if ($kind === 'FLAG_WING_WEAPON') {
                $changed = save_stat_rows($pdo, 'support_item_stat', 'support_id', $id, $_POST) > 0 || $changed;
            }
            if ($changed) {
                bump($pdo, 'SUPPORT');
            }
            flash($changed ? 'Đã lưu.' : 'Không có thay đổi.');
        });
        redirect('?p=support&id=' . $id);
    }

    layout_start($it['name_vi'], 'supports');
    echo '<p><a href="?p=supports">&larr; Danh sách đồ hỗ trợ</a></p><h1>' . h($it['name_vi']) . ' <span class="muted">#' . h($id) . ' · ' . h($kind) . '</span></h1>';
    echo '<form method="post" action="?p=support&amp;id=' . h($id) . '">' . csrf_field() . '<table><tbody>';
    foreach ($base as $c) {
        echo field_row($c['label'], render_input($c, $c['name'], $it[$c['name']]));
    }
    if ($extra) {
        $r = $pdo->prepare('SELECT * FROM `' . $extra['table'] . '` WHERE support_id = ?');
        $r->execute([$id]);
        $row = $r->fetch() ?: [];
        foreach ($extra['cols'] as $c) {
            echo field_row($c['label'], render_input($c, $c['name'], $row[$c['name']] ?? null));
        }
    }
    echo '</tbody></table>';
    if ($kind === 'FLAG_WING_WEAPON') {
        stat_rows_form($pdo, 'support_item_stat', 'support_id', $id);
    }
    echo '<p><button type="submit">Lưu</button></p></form>';
    layout_end();
}

register_page('supports', 'Đồ hỗ trợ', 'item', 'page_supports', 5);
register_hidden('support', 'page_support', 'supports');
register_hidden('support_new', 'page_support_new', 'supports');
