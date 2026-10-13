<?php
// BattleBall admin: grid screens of the group "Rơi đồ & Thưởng".
declare(strict_types=1);

/** Grid screens of the group "Rơi đồ & Thưởng" (spec format: see grid_page() in lib.php). */
function grids_drop(PDO $pdo): array
{
    $races = ['EARTH' => 'Trái Đất', 'NAMEC' => 'Namếc', 'XAYDA' => 'Xayda', 'ANY' => 'Mọi chủng tộc'];
    $specs = [
        'droprules' => [
            'table' => 'drop_rule', 'title' => 'Bảng rơi nguyên liệu', 'domain' => 'DROP', 'pk' => ['rule_id'], 'has_updated_by' => true,
            'insert_key' => ['source_class', 'tier', 'material_template_id', 'dungeon_id'],
            'intro' => 'Tỉ lệ rơi (%) mỗi lần hạ, trước khi nhân hệ số của phó bản. Boss thường (BOSS) tính theo cấp vùng của phó bản; MINI và FINAL theo vai trò; MOB và ELITE cho quái nhỏ. Vàng không nằm ở đây mà ở tham số "gold_split" của màn hình Cân bằng.',
            'select_sql' => "SELECT r.rule_id, r.source_class, r.tier, m.name_vi AS material, d.name_vi AS dungeon, r.rate_pct, r.qty_min, r.qty_max, r.enabled, r.note FROM drop_rule r JOIN material_template m ON m.template_id = r.material_template_id LEFT JOIN dungeon_template d ON d.dungeon_id = r.dungeon_id ORDER BY r.material_template_id, FIELD(r.source_class, 'MOB', 'ELITE', 'BOSS', 'MINI', 'FINAL'), r.tier",
            'cols' => [
                ['name' => 'source_class', 'label' => 'Nguồn', 'type' => 'ro'], ['name' => 'tier', 'label' => 'Cấp vùng', 'type' => 'ro'], ['name' => 'material', 'label' => 'Nguyên liệu', 'type' => 'ro'], ['name' => 'dungeon', 'label' => 'Chỉ phó bản', 'type' => 'ro'],
                ['name' => 'rate_pct', 'label' => 'Tỉ lệ (%)', 'type' => 'num'], ['name' => 'qty_min', 'label' => 'Số lượng nhỏ nhất', 'type' => 'int'],
                ['name' => 'qty_max', 'label' => 'Số lượng lớn nhất', 'type' => 'int'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
                ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
            'insert' => [
                ['name' => 'source_class', 'label' => 'Nguồn', 'type' => 'select', 'options' => ['MOB' => 'MOB (mob thường)', 'ELITE' => 'ELITE (mob elite)', 'BOSS' => 'BOSS (boss thường)', 'MINI' => 'MINI (mini-boss)', 'FINAL' => 'FINAL (boss cuối)']],
                ['name' => 'tier', 'label' => 'Cấp vùng', 'type' => 'select', 'options' => ['ANY' => 'Mọi cấp', 'early' => 'Vùng sớm', 'mid' => 'Vùng giữa', 'late' => 'Vùng cuối']],
                ['name' => 'material_template_id', 'label' => 'Nguyên liệu', 'type' => 'select', 'options' => lookup($pdo, 'SELECT template_id, CONCAT(name_vi, \' (#\', template_id, \')\') FROM material_template ORDER BY template_id')],
                ['name' => 'dungeon_id', 'label' => 'Chỉ phó bản (để trống = mọi phó bản)', 'type' => 'select', 'null' => true, 'options' => ['' => '—'] + lookup($pdo, 'SELECT dungeon_id, name_vi FROM dungeon_template ORDER BY sort_order')],
                ['name' => 'rate_pct', 'label' => 'Tỉ lệ (%)', 'type' => 'num'], ['name' => 'qty_min', 'label' => 'Số lượng nhỏ nhất', 'type' => 'int', 'default' => '1'],
                ['name' => 'qty_max', 'label' => 'Số lượng lớn nhất', 'type' => 'int', 'default' => '1'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'],
                ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM drop_rule WHERE rate_pct < 0 OR rate_pct > 100 OR qty_min > qty_max OR qty_min < 1')->fetchColumn() > 0) {
                    throw new RuntimeException('Tỉ lệ phải từ 0 đến 100 và số lượng nhỏ nhất (từ 1) không được lớn hơn lớn nhất.');
                }
            },
        ],
        'dropmult' => [
            'table' => 'dungeon_drop_mult', 'title' => 'Hệ số rơi nguyên liệu theo phó bản', 'domain' => 'DROP', 'pk' => ['dungeon_id', 'material_template_id'], 'has_updated_by' => true,
            'intro' => 'Nhân tỉ lệ rơi của một nguyên liệu trong một phó bản (không có dòng nghĩa là 1). Ví dụ phó bản "Farm Sách tuyệt kỹ" nhân 3 cho nguyên liệu sách. Tỉ lệ sau khi nhân tối đa 100%.',
            'select_sql' => 'SELECT x.dungeon_id, d.name_vi AS dungeon, x.material_template_id, m.name_vi AS material, x.mult FROM dungeon_drop_mult x JOIN dungeon_template d USING (dungeon_id) JOIN material_template m ON m.template_id = x.material_template_id ORDER BY x.dungeon_id, x.material_template_id',
            'cols' => [['name' => 'dungeon', 'label' => 'Phó bản', 'type' => 'ro'], ['name' => 'material', 'label' => 'Nguyên liệu', 'type' => 'ro'], ['name' => 'mult', 'label' => 'Hệ số', 'type' => 'num']],
            'insert' => [
                ['name' => 'dungeon_id', 'label' => 'Phó bản', 'type' => 'select', 'options' => lookup($pdo, 'SELECT dungeon_id, name_vi FROM dungeon_template ORDER BY sort_order')],
                ['name' => 'material_template_id', 'label' => 'Nguyên liệu', 'type' => 'select', 'options' => lookup($pdo, 'SELECT template_id, name_vi FROM material_template ORDER BY template_id')],
                ['name' => 'mult', 'label' => 'Hệ số', 'type' => 'num'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM dungeon_drop_mult WHERE mult <= 0')->fetchColumn() > 0) {
                    throw new RuntimeException('Hệ số phải lớn hơn 0.');
                }
            },
        ],
        'gemrewards' => [
            'table' => 'gem_reward_rule', 'title' => 'Nguồn đá quý', 'domain' => 'GEM', 'pk' => ['rule_id'], 'has_updated_by' => true,
            'insert_key' => ['reward_type', 'dungeon_id'],
            'intro' => 'Đá quý chỉ nhận từ các nguồn này (không mua bằng vàng, quái không rơi). Lần đầu hạ boss cuối: một lần cho mỗi nhân vật và phó bản. Phó bản giới hạn thời gian: đặt thời gian ở màn hình "Phó bản". Raid: chưa có phó bản raid nên đang tắt. "Giới hạn mỗi ngày" tính theo từng nhân vật, ngày bắt đầu 00:00 giờ Việt Nam.',
            'select_sql' => 'SELECT r.rule_id, r.reward_type, d.name_vi AS dungeon, r.gems, r.daily_cap, r.enabled, r.note FROM gem_reward_rule r LEFT JOIN dungeon_template d ON d.dungeon_id = r.dungeon_id ORDER BY FIELD(r.reward_type, \'FIRST_CLEAR\', \'TIMED_CLEAR\', \'RAID_FIRST\', \'RAID_REPEAT\'), r.dungeon_id',
            'cols' => [
                ['name' => 'reward_type', 'label' => 'Nguồn', 'type' => 'ro'], ['name' => 'dungeon', 'label' => 'Chỉ phó bản', 'type' => 'ro'],
                ['name' => 'gems', 'label' => 'Đá quý', 'type' => 'int'], ['name' => 'daily_cap', 'label' => 'Giới hạn mỗi ngày', 'type' => 'int', 'null' => true],
                ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'], ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
            'insert' => [
                ['name' => 'reward_type', 'label' => 'Nguồn', 'type' => 'select', 'options' => ['FIRST_CLEAR' => 'FIRST_CLEAR (lần đầu hạ boss cuối)', 'TIMED_CLEAR' => 'TIMED_CLEAR (phó bản giới hạn thời gian)', 'RAID_FIRST' => 'RAID_FIRST', 'RAID_REPEAT' => 'RAID_REPEAT']],
                ['name' => 'dungeon_id', 'label' => 'Chỉ phó bản (để trống = mọi phó bản)', 'type' => 'select', 'null' => true, 'options' => ['' => '—'] + lookup($pdo, 'SELECT dungeon_id, name_vi FROM dungeon_template ORDER BY sort_order')],
                ['name' => 'gems', 'label' => 'Đá quý', 'type' => 'int'], ['name' => 'daily_cap', 'label' => 'Giới hạn mỗi ngày', 'type' => 'int', 'null' => true],
                ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'], ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM gem_reward_rule WHERE gems < 1')->fetchColumn() > 0) {
                    throw new RuntimeException('Số đá quý phải từ 1.');
                }
            },
        ],
    ];
    return $specs;
}

register_grids('drop', 'grids_drop', [
    'droprules' => 'Bảng rơi đồ',
    'dropmult' => 'Hệ số rơi phó bản',
    'gemrewards' => 'Nguồn đá quý',
]);
register_link('drop', 'drop_picker.html', 'Chọn boss rơi đồ', 100);
