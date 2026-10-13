<?php
// BattleBall admin: monsters of the dungeons.
declare(strict_types=1);

function page_monsters(PDO $pdo): void
{
    $dungeons = lookup($pdo, 'SELECT dungeon_id, name_vi FROM dungeon_template ORDER BY sort_order');
    $sel = (int)($_GET['dungeon'] ?? 0);
    $st = $pdo->prepare('SELECT dungeon, tier, boss, role, class_key, hp, atk_per_hit, armor_reduction_pct, action_gap_s FROM v_dungeon_monster' . ($sel ? ' WHERE dungeon_id = ?' : '') . ' ORDER BY dungeon_id, boss_id');
    $st->execute($sel ? [$sel] : []);
    $rows = $st->fetchAll();
    layout_start('Quái trong phó bản', 'monsters');
    echo '<h1>Quái trong từng phó bản (chỉ số cuối cùng)</h1><p class="muted">Tính từ loại quái, cấp vùng và hệ số từng boss. Chỉ xem; sửa ở "Chỉ số quái" và "Hệ số boss".</p>'
        . '<form method="get" class="filter"><input type="hidden" name="p" value="monsters"><select name="dungeon"><option value="0">Mọi phó bản</option>';
    foreach ($dungeons as $k => $lab) {
        echo '<option value="' . h($k) . '"' . ((int)$k === $sel ? ' selected' : '') . '>' . h($lab) . '</option>';
    }
    echo '</select><button type="submit">Xem</button> <span class="muted">' . count($rows) . ' dòng</span></form>'
        . '<div class="scroll"><table><thead><tr><th>Phó bản</th><th>Cấp</th><th>Quái</th><th>Vai trò</th><th>Loại</th><th>HP</th><th>ATK mỗi đòn</th><th>Giáp (%)</th><th>Giãn cách (s)</th></tr></thead><tbody>';
    foreach ($rows as $r) {
        echo '<tr><td>' . h($r['dungeon']) . '</td><td>' . h($r['tier']) . '</td><td>' . h($r['boss']) . '</td><td>' . h($r['role']) . '</td><td>' . h($r['class_key'])
            . '</td><td style="text-align:right">' . h(number_format((float)$r['hp'], 0, ',', '.')) . '</td><td style="text-align:right">' . h(number_format((float)$r['atk_per_hit'], 0, ',', '.'))
            . '</td><td>' . h(rtrim(rtrim(number_format((float)$r['armor_reduction_pct'], 2, '.', ''), '0'), '.')) . '</td><td>' . h($r['action_gap_s']) . '</td></tr>';
    }
    echo '</tbody></table></div>';
    layout_end();
}

register_page('monsters', 'Quái phó bản', 'dungeon', 'page_monsters', 15);
