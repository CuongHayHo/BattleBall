<?php
// BattleBall admin: grid screens of the group "Skin & Avatar".
declare(strict_types=1);

/** Grid screens of the group "Skin & Avatar" (spec format: see grid_page() in lib.php). */
function grids_skin(PDO $pdo): array
{
    $specs = [
        'stats' => [
            'table' => 'stat_type', 'title' => 'Loại chỉ số của skin', 'domain' => 'STAT', 'pk' => ['stat_key'], 'order' => 'sort_order', 'has_updated_by' => true,
            'intro' => 'Bật hoặc tắt từng loại chỉ số. Server chỉ áp dụng chỉ số của skin và đồ hỗ trợ khi loại đó đang bật. "Mức gợi ý" chỉ để cảnh báo, không chặn.',
            'cols' => [
                ['name' => 'stat_key', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'ro'],
                ['name' => 'unit', 'label' => 'Đơn vị', 'type' => 'ro'], ['name' => 'applies', 'label' => 'Áp dụng', 'type' => 'ro'],
                ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
                ['name' => 'suggested_max', 'label' => 'Mức gợi ý tối đa', 'type' => 'num', 'null' => true],
                ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
        ],
        'kinds' => [
            'table' => 'skin_kind', 'title' => 'Loại skin', 'domain' => 'SKIN', 'pk' => ['kind_key'], 'order' => 'sort_order', 'has_updated_by' => true,
            'intro' => 'Cờ của từng loại: bán được, người chơi mặc được, chỉ nhân vật chủ khi hợp thể. Thêm loại mới bằng một dòng SQL.',
            'cols' => [
                ['name' => 'kind_key', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 80],
                ['name' => 'sellable', 'label' => 'Bán được', 'type' => 'bool'], ['name' => 'player_wearable', 'label' => 'Người chơi mặc', 'type' => 'bool'],
                ['name' => 'requires_fusion_main', 'label' => 'Chỉ nhân vật chủ hợp thể', 'type' => 'bool'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
                ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
            ],
        ],
        'rarity' => [
            'table' => 'skin_rarity', 'title' => 'Độ hiếm của skin', 'domain' => 'SKIN', 'pk' => ['rarity_key'], 'order' => 'sort_order', 'has_updated_by' => true,
            'intro' => 'Giá đá quý của skin = giá cơ sở của nhóm × hệ số giá của độ hiếm. Đổi ở đây chỉ ảnh hưởng skin tạo mới; giá từng skin sửa ở màn hình Skin.',
            'cols' => [
                ['name' => 'rarity_key', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 40],
                ['name' => 'price_mult', 'label' => 'Hệ số giá', 'type' => 'num'], ['name' => 'default_stat_pct', 'label' => 'Chỉ số mặc định (%)', 'type' => 'num'],
            ],
        ],
        'groups' => [
            'table' => 'skin_group_price', 'title' => 'Giá cơ sở theo nhóm skin', 'domain' => 'SKIN', 'pk' => ['body_group'], 'has_updated_by' => true,
            'intro' => 'Giá cơ sở (đá quý) của từng nhóm thân hình.',
            'cols' => [['name' => 'body_group', 'label' => 'Nhóm', 'type' => 'ro'], ['name' => 'base_price', 'label' => 'Giá cơ sở', 'type' => 'int']],
        ],
        'defaultskins' => [
            'table' => 'race_default_skin', 'title' => 'Avatar nhận khi tạo nhân vật', 'domain' => 'SKIN', 'pk' => ['race', 'costume_id'], 'has_updated_by' => true,
            'intro' => 'Mỗi chủng tộc nhận các avatar này khi tạo nhân vật và mặc cái được đánh dấu. Mỗi chủng tộc phải có đúng một avatar được mặc.',
            'cols' => [['name' => 'race', 'label' => 'Chủng tộc', 'type' => 'ro'], ['name' => 'costume_id', 'label' => 'Mã skin', 'type' => 'ro'], ['name' => 'is_equipped', 'label' => 'Mặc ngay', 'type' => 'bool']],
            'validate' => function (PDO $pdo) {
                $bad = $pdo->query('SELECT race FROM race_default_skin GROUP BY race HAVING SUM(is_equipped) <> 1')->fetchAll(PDO::FETCH_COLUMN);
                if ($bad) {
                    throw new RuntimeException('Mỗi chủng tộc phải có đúng một avatar mặc ngay (lỗi ở: ' . implode(', ', $bad) . ').');
                }
            },
        ],
    ];
    $keyPat = ['pattern' => '/^[A-Z][A-Z0-9_]{1,23}$/', 'hint' => 'chữ in hoa, số, _; tối đa 24 ký tự'];
    $specs['kinds']['insert'] = [
        ['name' => 'kind_key', 'label' => 'Mã loại', 'type' => 'text', 'max' => 24] + $keyPat,
        ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 80],
        ['name' => 'sellable', 'label' => 'Bán được', 'type' => 'bool'], ['name' => 'player_wearable', 'label' => 'Người chơi mặc', 'type' => 'bool'],
        ['name' => 'requires_fusion_main', 'label' => 'Chỉ nhân vật chủ hợp thể', 'type' => 'bool'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'],
        ['name' => 'note', 'label' => 'Ghi chú', 'type' => 'text', 'null' => true],
    ];
    $specs['rarity']['insert'] = [
        ['name' => 'rarity_key', 'label' => 'Mã độ hiếm', 'type' => 'text', 'max' => 16, 'pattern' => '/^[A-Z][A-Z0-9_]{1,15}$/', 'hint' => 'chữ in hoa, số, _; tối đa 16 ký tự'],
        ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 40],
        ['name' => 'price_mult', 'label' => 'Hệ số giá', 'type' => 'num'], ['name' => 'default_stat_pct', 'label' => 'Chỉ số mặc định (%)', 'type' => 'num'],
    ];
    $specs['groups']['insert'] = [
        ['name' => 'body_group', 'label' => 'Nhóm', 'type' => 'text', 'max' => 16, 'pattern' => '/^[A-Za-z][A-Za-z0-9_]{1,15}$/', 'hint' => 'chữ, số, _; tối đa 16 ký tự'],
        ['name' => 'base_price', 'label' => 'Giá cơ sở', 'type' => 'int'],
    ];
    $specs['defaultskins']['insert'] = [
        ['name' => 'race', 'label' => 'Chủng tộc', 'type' => 'select', 'options' => ['EARTH' => 'Trái Đất', 'NAMEC' => 'Namếc', 'XAYDA' => 'Xayda']],
        ['name' => 'costume_id', 'label' => 'Mã skin (loại avatar)', 'type' => 'int'], ['name' => 'is_equipped', 'label' => 'Mặc ngay', 'type' => 'bool'],
    ];
    return $specs;
}

register_grids('skin', 'grids_skin', [
    'stats' => 'Chỉ số',
    'kinds' => 'Loại skin',
    'rarity' => 'Độ hiếm',
    'groups' => 'Giá theo nhóm',
    'defaultskins' => 'Avatar mặc định',
]);
