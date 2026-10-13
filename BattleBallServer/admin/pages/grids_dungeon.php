<?php
// BattleBall admin: grid screens of the group "Phó bản & Quái".
declare(strict_types=1);

/** Grid screens of the group "Phó bản & Quái" (spec format: see grid_page() in lib.php). */
function grids_dungeon(PDO $pdo): array
{
    $specs = [
        'dungeons' => [
            'table' => 'dungeon_template', 'title' => 'Phó bản', 'domain' => 'DUNGEON', 'pk' => ['dungeon_id'], 'has_updated_by' => true,
            'intro' => 'Mỗi phó bản dùng một nhóm boss (chọn nhóm bằng trang "Nhóm boss phó bản"; đổi nhóm của phó bản hiện phải sửa bằng SQL). Cấp vùng quyết định độ mạnh của boss theo GDD mục 7. Mỗi trận luôn có boss cuối (và mini-boss nếu có), còn lại bốc ngẫu nhiên không lặp từ boss thường cho đủ "Số boss mỗi trận"; quái nhỏ không tính.',
            'select_sql' => 'SELECT d.dungeon_id, d.name_vi, d.tier, d.bosses_per_match, d.time_limit_s, g.label_vi AS group_label, d.enabled, d.note, d.sort_order FROM dungeon_template d LEFT JOIN boss_group g USING (group_key) ORDER BY d.sort_order',
            'cols' => [
                ['name' => 'dungeon_id', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120],
                ['name' => 'tier', 'label' => 'Cấp vùng', 'type' => 'select', 'options' => ['early' => 'Vùng sớm', 'mid' => 'Vùng giữa', 'late' => 'Vùng cuối']],
                ['name' => 'bosses_per_match', 'label' => 'Số boss mỗi trận', 'type' => 'int'],
                ['name' => 'time_limit_s', 'label' => 'Giới hạn thời gian (giây, trống = không)', 'type' => 'int', 'null' => true],
                ['name' => 'group_label', 'label' => 'Nhóm boss', 'type' => 'ro'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
                ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true], ['name' => 'sort_order', 'label' => 'Thứ tự', 'type' => 'int'],
            ],
        ],
        'dungeonbosses' => [
            'table' => 'dungeon_boss', 'title' => 'Boss của từng phó bản', 'domain' => 'DUNGEON', 'pk' => ['dungeon_id', 'boss_id'], 'has_updated_by' => true,
            'intro' => 'Vai trò: NORMAL (thường), MINI (mini-boss cuối tầng), FINAL (boss cuối phó bản), MINION (quái nhỏ ra ồ ạt trong trận). Hiện mọi boss để NORMAL vì chưa đánh dấu; mỗi phó bản nên có ít nhất một FINAL.',
            'select_sql' => 'SELECT x.dungeon_id, d.name_vi AS dungeon, x.boss_id, b.name_vi AS boss, x.role, x.mob_tier, x.sort_order FROM dungeon_boss x JOIN dungeon_template d USING (dungeon_id) JOIN boss_template b USING (boss_id) ORDER BY x.dungeon_id, x.sort_order',
            'cols' => [
                ['name' => 'dungeon', 'label' => 'Phó bản', 'type' => 'ro'], ['name' => 'boss', 'label' => 'Boss', 'type' => 'ro'],
                ['name' => 'role', 'label' => 'Vai trò', 'type' => 'select', 'options' => ['NORMAL' => 'NORMAL', 'MINI' => 'MINI', 'FINAL' => 'FINAL', 'MINION' => 'MINION']],
                ['name' => 'mob_tier', 'label' => 'Loại quái nhỏ', 'type' => 'select', 'null' => true, 'options' => ['' => '—', 'MOB' => 'Mob thường', 'ELITE' => 'Mob elite']],
                ['name' => 'sort_order', 'label' => 'Thứ tự', 'type' => 'int'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query("SELECT COUNT(*) FROM dungeon_boss WHERE (role = 'MINION' AND mob_tier IS NULL) OR (role <> 'MINION' AND mob_tier IS NOT NULL)")->fetchColumn() > 0) {
                    throw new RuntimeException('Chỉ vai trò MINION mới có "Loại quái nhỏ", và mọi MINION phải chọn mob thường hoặc elite.');
                }
            },
        ],
        'bossgroups' => [
            'table' => 'boss_group', 'title' => 'Nhóm boss và nhóm quái nhỏ', 'domain' => 'DUNGEON', 'pk' => ['group_key'], 'has_updated_by' => true, 'order' => 'tag, label_vi',
            'intro' => 'Nhóm thẻ MOB là quái nhỏ ra ồ ạt; "Loại quái nhỏ" cho biết chỉ số theo mob thường hay mob elite (GDD mục 7.1).',
            'cols' => [
                ['name' => 'group_key', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'label_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120],
                ['name' => 'tag', 'label' => 'Thẻ', 'type' => 'ro'],
                ['name' => 'mob_tier', 'label' => 'Loại quái nhỏ', 'type' => 'select', 'null' => true, 'options' => ['' => '—', 'MOB' => 'Mob thường', 'ELITE' => 'Mob elite']],
                ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query("SELECT COUNT(*) FROM boss_group WHERE (tag = 'MOB' AND mob_tier IS NULL) OR (tag <> 'MOB' AND mob_tier IS NOT NULL)")->fetchColumn() > 0) {
                    throw new RuntimeException('Chỉ nhóm thẻ MOB mới có "Loại quái nhỏ", và mọi nhóm MOB phải chọn loại.');
                }
            },
        ],
        'monsterstats' => [
            'table' => 'monster_stat_class', 'title' => 'Chỉ số quái theo loại và cấp vùng', 'domain' => 'MONSTER', 'pk' => ['class_key', 'tier'], 'has_updated_by' => true,
            'order' => "FIELD(class_key, 'MOB', 'ELITE', 'BOSS', 'MINI', 'FINAL'), FIELD(tier, 'early', 'mid', 'late')",
            'intro' => 'HP tính từ thời gian hạ x sức mạnh đội dự kiến của cấp vùng x sát thương/giây tối đa của đội x (1 - giáp), theo GDD mục 7. ATK là % của Max_ATK_Total mỗi đòn (ngẫu nhiên trong khoảng); giáp là % giảm sát thương của quái. "Giãn cách chiêu" là số giây giữa hai hành động của boss.',
            'cols' => [
                ['name' => 'class_key', 'label' => 'Loại', 'type' => 'ro'], ['name' => 'tier', 'label' => 'Cấp vùng', 'type' => 'ro'],
                ['name' => 'ttk_seconds', 'label' => 'Thời gian hạ (s)', 'type' => 'num'], ['name' => 'hp', 'label' => 'HP', 'type' => 'int'],
                ['name' => 'atk_pct_min', 'label' => 'ATK nhỏ nhất (%)', 'type' => 'num'], ['name' => 'atk_pct_max', 'label' => 'ATK lớn nhất (%)', 'type' => 'num'],
                ['name' => 'armor_pct_min', 'label' => 'Giáp nhỏ nhất (%)', 'type' => 'num'], ['name' => 'armor_pct_max', 'label' => 'Giáp lớn nhất (%)', 'type' => 'num'],
                ['name' => 'action_gap_s', 'label' => 'Giãn cách chiêu (s)', 'type' => 'num', 'null' => true], ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM monster_stat_class WHERE hp < 1 OR atk_pct_min > atk_pct_max OR armor_pct_min > armor_pct_max OR armor_pct_max >= 100 OR atk_pct_min < 0')->fetchColumn() > 0) {
                    throw new RuntimeException('HP phải từ 1, ATK và giáp nhỏ nhất không được lớn hơn lớn nhất, giáp dưới 100%.');
                }
            },
        ],
        'bossmult' => [
            'table' => 'boss_template', 'title' => 'Hệ số HP và ATK của từng boss', 'domain' => 'MONSTER', 'pk' => ['boss_id'], 'has_updated_by' => true,
            'intro' => 'Nhân HP và ATK của một boss so với chỉ số chung của loại (1 là bằng chung). Dùng để làm một boss yếu hoặc mạnh hơn các boss cùng loại.',
            'select_sql' => 'SELECT b.boss_id, g.label_vi AS grp, b.name_vi, b.hp_mult, b.atk_mult, b.enabled FROM boss_template b JOIN boss_group g USING (group_key) ORDER BY g.label_vi, b.boss_id',
            'cols' => [
                ['name' => 'grp', 'label' => 'Nhóm', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Boss', 'type' => 'text', 'max' => 120],
                ['name' => 'hp_mult', 'label' => 'Hệ số HP', 'type' => 'num'], ['name' => 'atk_mult', 'label' => 'Hệ số ATK', 'type' => 'num'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM boss_template WHERE hp_mult <= 0 OR atk_mult <= 0')->fetchColumn() > 0) {
                    throw new RuntimeException('Hệ số phải lớn hơn 0.');
                }
            },
        ],
    ];
    return $specs;
}

register_grids('dungeon', 'grids_dungeon', [
    'dungeons' => 'Phó bản',
    'dungeonbosses' => 'Boss phó bản',
    'bossgroups' => 'Nhóm boss',
    'monsterstats' => 'Chỉ số quái',
    'bossmult' => 'Hệ số boss',
]);
register_link('dungeon', 'boss_group_picker.html', 'Chọn nhóm boss', 100);
