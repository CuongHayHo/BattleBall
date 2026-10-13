<?php
// BattleBall admin: the stats of the skins sold in the shop of Urôn, all in one table (a skin per row, a stat type per column).
// A skin is on sale under the same rule as the game server (game/ShopService.java, EconomyService.buySkin): skin enabled, its kind enabled and
// sellable, a gem price set. Stats live in skin_stat (the server reads them live, an owned skin has no copy), so a saved change reaches the players
// when the server reloads the SKIN data (it checks static_data_version every 30 seconds). Every change goes to admin_audit_log.
declare(strict_types=1);

const SHOPSKIN_PAGE_SIZE = 40;

/** Writes the cells of the posted skins; returns the number of changed cells. An empty cell deletes the stat, a number inserts or updates it. */
function save_shop_skin_stats(PDO $pdo, array $post): int
{
    $changes = 0;
    $types = [];
    foreach ($pdo->query('SELECT stat_key, suggested_max, enabled FROM stat_type') as $r) {
        $types[$r['stat_key']] = $r;
    }
    $ids = array_map('intval', (array)($post['ids'] ?? []));
    $cells = (array)($post['v'] ?? []);
    $cur = $pdo->prepare('SELECT stat_key, `value` FROM skin_stat WHERE costume_id = ?');
    foreach ($ids as $id) {
        $cur->execute([$id]);
        $now = [];
        foreach ($cur->fetchAll() as $row) {
            $now[$row['stat_key']] = $row['value'];
        }
        foreach ((array)($cells[$id] ?? []) as $k => $raw) {
            if (!isset($types[$k]) || !$types[$k]['enabled']) {
                continue;                                  // only the columns shown (enabled stat types)
            }
            $v = trim((string)$raw);
            if ($v === '') {
                if (isset($now[$k])) {
                    $pdo->prepare('DELETE FROM skin_stat WHERE costume_id = ? AND stat_key = ?')->execute([$id, $k]);
                    audit($pdo, 'skin_stat', "$id:$k", 'DELETE', ['value' => $now[$k]], null);
                    $changes++;
                }
                continue;
            }
            if (!is_numeric($v)) {
                throw new RuntimeException("Skin $id, chỉ số $k: \"$v\" không phải số.");
            }
            if (isset($now[$k])) {
                if (abs((float)$v - (float)$now[$k]) > 1e-9) {
                    $pdo->prepare('UPDATE skin_stat SET `value` = ?, updated_by = ? WHERE costume_id = ? AND stat_key = ?')->execute([$v, admin_name(), $id, $k]);
                    audit($pdo, 'skin_stat', "$id:$k", 'UPDATE', ['value' => $now[$k]], ['value' => $v]);
                    $changes++;
                } else {
                    continue;
                }
            } else {
                $pdo->prepare('INSERT INTO skin_stat (costume_id, stat_key, `value`, updated_by) VALUES (?,?,?,?)')->execute([$id, $k, $v, admin_name()]);
                audit($pdo, 'skin_stat', "$id:$k", 'INSERT', null, ['value' => $v]);
                $changes++;
            }
            $max = $types[$k]['suggested_max'];
            if ($max !== null && (float)$v > (float)$max) {
                flash("Cảnh báo: skin $id, $k = $v vượt mức gợi ý $max.", 'warn');
            }
        }
    }
    return $changes;
}

function page_shop_skins(PDO $pdo): void
{
    $gender = $_GET['gender'] ?? '';
    $kind = (string)($_GET['kind'] ?? '');
    $q = trim((string)($_GET['q'] ?? ''));
    $only = ($_GET['cols'] ?? 'all') === 'used' ? 'used' : 'all';
    $pg = max(1, (int)($_GET['pg'] ?? 1));
    $keep = ['p' => 'shopskins', 'gender' => $gender, 'kind' => $kind, 'q' => $q, 'cols' => $only, 'pg' => $pg];

    $where = ['t.enabled = 1', 'k.enabled = 1', 'k.sellable = 1', 't.gem_price IS NOT NULL'];
    $args = [];
    if ($gender !== '' && ctype_digit((string)$gender)) {
        $where[] = 't.gender_id = ?';
        $args[] = (int)$gender;
    }
    if ($kind !== '') {
        $where[] = 't.kind_key = ?';
        $args[] = $kind;
    }
    if ($q !== '') {
        $where[] = '(t.name LIKE ? OR t.costume_id = ?)';
        $args[] = '%' . $q . '%';
        $args[] = ctype_digit($q) ? (int)$q : -1;
    }
    $from = ' FROM skin_template t JOIN skin_kind k ON k.kind_key = t.kind_key WHERE ' . implode(' AND ', $where);

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $changes = 0;
        $ok = in_tx($pdo, function () use ($pdo, &$changes) {
            $changes = save_shop_skin_stats($pdo, $_POST);
            if ($changes > 0) {
                bump($pdo, 'SKIN');
            }
        });
        if ($ok) {
            flash($changes > 0 ? "Đã lưu $changes ô chỉ số. Game server nạp lại trong tối đa 30 giây." : 'Không có thay đổi.');
        }
        redirect('?' . http_build_query($keep));
    }

    $total = (int)(function () use ($pdo, $from, $args) {
        $st = $pdo->prepare('SELECT COUNT(*)' . $from);
        $st->execute($args);
        return $st->fetchColumn();
    })();
    $pages = max(1, (int)ceil($total / SHOPSKIN_PAGE_SIZE));
    $pg = min($pg, $pages);
    $keep['pg'] = $pg;
    $st = $pdo->prepare('SELECT t.costume_id, t.name, t.kind_key, t.rarity_key, t.gem_price, t.gender_name' . $from
        . ' ORDER BY t.gem_price, t.costume_id LIMIT ' . SHOPSKIN_PAGE_SIZE . ' OFFSET ' . (($pg - 1) * SHOPSKIN_PAGE_SIZE));
    $st->execute($args);
    $skins = $st->fetchAll();

    $statSql = 'SELECT stat_key, name_vi, unit, suggested_max FROM stat_type WHERE enabled = 1'
        . ($only === 'used' ? ' AND stat_key IN (SELECT DISTINCT stat_key FROM skin_stat)' : '') . ' ORDER BY sort_order';
    $stats = $pdo->query($statSql)->fetchAll();
    $values = [];
    if ($skins) {
        $in = implode(',', array_map(fn($s) => (int)$s['costume_id'], $skins));
        foreach ($pdo->query("SELECT costume_id, stat_key, `value` FROM skin_stat WHERE costume_id IN ($in)") as $r) {
            $values[(int)$r['costume_id']][$r['stat_key']] = $r['value'];
        }
    }
    $kinds = lookup($pdo, 'SELECT k.kind_key, k.name_vi FROM skin_kind k WHERE k.sellable = 1 AND k.enabled = 1 ORDER BY k.sort_order');
    $genders = lookup($pdo, 'SELECT DISTINCT gender_id, gender_name FROM skin_template ORDER BY gender_id');

    layout_start('Skin bán ở shop Urôn', 'shopskins');
    echo '<h1>Skin bán ở shop Urôn</h1><p class="muted">Chỉ có skin đang bán: skin bật, loại skin bật và bán được, có giá đá quý. Mỗi hàng một skin, mỗi cột một loại chỉ số: '
        . 'ô trống là không có chỉ số, xóa số trong ô để bỏ chỉ số. Chỉ số nằm ở bảng <code>skin_stat</code> và áp dụng cho cả skin người chơi đã mua. '
        . 'Tên, giá và loại skin sửa ở trang <a href="?p=skins">Skin</a>.</p>';
    echo '<form method="get" class="filter"><input type="hidden" name="p" value="shopskins"><select name="gender"><option value="">Mọi tộc</option>';
    foreach ($genders as $gid => $gname) {
        echo '<option value="' . h($gid) . '"' . ((string)$gid === (string)$gender ? ' selected' : '') . '>' . h($gname) . '</option>';
    }
    echo '</select><select name="kind"><option value="">Mọi loại</option>';
    foreach ($kinds as $k => $lab) {
        echo '<option value="' . h($k) . '"' . ($k === $kind ? ' selected' : '') . '>' . h($lab) . '</option>';
    }
    echo '</select><select name="cols"><option value="all"' . ($only === 'all' ? ' selected' : '') . '>Mọi cột chỉ số</option><option value="used"'
        . ($only === 'used' ? ' selected' : '') . '>Chỉ cột đang có skin dùng</option></select>'
        . '<input type="text" name="q" value="' . h($q) . '" placeholder="Tên hoặc mã skin"><button type="submit">Lọc</button> <span class="muted">' . $total . ' skin, trang ' . $pg . '/' . $pages . '</span></form>';

    if (!$skins) {
        echo '<p>Không có skin nào khớp.</p>';
        layout_end();
        return;
    }
    echo '<form method="post" action="?' . h(http_build_query($keep)) . '">' . csrf_field() . '<div class="scroll"><table class="matrix"><thead><tr>'
        . '<th class="stick">Skin</th><th>Tộc</th><th>Loại</th><th>Giá</th>';
    foreach ($stats as $s) {
        echo '<th title="' . h($s['stat_key'] . ($s['suggested_max'] !== null ? ' (gợi ý tối đa ' . $s['suggested_max'] . ')' : '')) . '">' . h($s['name_vi']) . '</th>';
    }
    echo '</tr></thead><tbody>';
    foreach ($skins as $sk) {
        $id = (int)$sk['costume_id'];
        echo '<tr><td class="stick"><input type="hidden" name="ids[]" value="' . $id . '"><a href="?p=skin&amp;id=' . $id . '">' . h($sk['name']) . '</a> <span class="muted">#' . $id . '</span></td>'
            . '<td>' . h($sk['gender_name']) . '</td><td>' . h($sk['kind_key']) . '</td><td>' . h($sk['gem_price']) . '</td>';
        foreach ($stats as $s) {
            $val = $values[$id][$s['stat_key']] ?? '';
            if ($val !== '') {
                $val = rtrim(rtrim((string)$val, '0'), '.');
            }
            echo '<td><input class="num" type="text" name="v[' . $id . '][' . h($s['stat_key']) . ']" value="' . h($val) . '" inputmode="decimal"></td>';
        }
        echo '</tr>';
    }
    echo '</tbody></table></div><p><button type="submit">Lưu trang này</button></p></form><p class="pager">';
    foreach ([['‹ Trước', $pg - 1], ['Sau ›', $pg + 1]] as [$label, $to]) {
        if ($to >= 1 && $to <= $pages) {
            echo '<a href="?' . h(http_build_query(array_merge($keep, ['pg' => $to]))) . '">' . $label . '</a> ';
        }
    }
    echo '</p>';
    layout_end();
}

register_page('shopskins', 'Skin shop Urôn', 'skin', 'page_shop_skins', 6);
