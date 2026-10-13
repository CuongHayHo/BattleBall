<?php
// BattleBall admin: grid screens of the group "Đồ hỗ trợ".
declare(strict_types=1);

/** Grid screens of the group "Đồ hỗ trợ" (spec format: see grid_page() in lib.php). */
function grids_item(PDO $pdo): array
{
    $specs = [
        'bookpool' => [
            'table' => 'skill_book_pool', 'title' => 'Chỉ số ngẫu nhiên của sách tuyệt kỹ', 'domain' => 'SUPPORT', 'pk' => ['stat_key'], 'has_updated_by' => true,
            'intro' => 'Khi giám định, sách chọn ngẫu nhiên một dòng theo trọng số; giá trị ngẫu nhiên trong khoảng nhỏ nhất đến lớn nhất.',
            'cols' => [
                ['name' => 'stat_key', 'label' => 'Chỉ số', 'type' => 'ro'], ['name' => 'min_value', 'label' => 'Nhỏ nhất', 'type' => 'num'],
                ['name' => 'max_value', 'label' => 'Lớn nhất', 'type' => 'num'], ['name' => 'weight', 'label' => 'Trọng số', 'type' => 'int'],
                ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM skill_book_pool WHERE min_value > max_value')->fetchColumn() > 0) {
                    throw new RuntimeException('Giá trị nhỏ nhất không được lớn hơn lớn nhất.');
                }
            },
        ],
        'recipes' => [
            'table' => 'craft_recipe', 'title' => 'Công thức ghép / nâng cấp sách tuyệt kỹ', 'domain' => 'SUPPORT', 'pk' => ['recipe_id'], 'has_updated_by' => true,
            'intro' => 'Ghép (CRAFT): dùng nguyên liệu tạo sách mới. Nâng cấp (UPGRADE): sách đang có đổi sang phiên bản 2. Nguyên liệu của từng công thức sửa ở màn hình "Nguyên liệu ghép". Nguyên liệu phụ (con dấu) nâng tỉ lệ thành công và có thể thêm dòng chỉ số.',
            'cols' => [
                ['name' => 'recipe_id', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120],
                ['name' => 'recipe_type', 'label' => 'Loại', 'type' => 'ro'], ['name' => 'input_support_id', 'label' => 'Sách đầu vào', 'type' => 'ro'],
                ['name' => 'result_support_id', 'label' => 'Sách kết quả', 'type' => 'ro'],
                ['name' => 'success_pct', 'label' => 'Thành công (%)', 'type' => 'num'], ['name' => 'boost_material_id', 'label' => 'Nguyên liệu phụ (mã)', 'type' => 'int', 'null' => true],
                ['name' => 'boost_success_pct', 'label' => 'Thành công khi có phụ (%)', 'type' => 'num', 'null' => true],
                ['name' => 'extra_line_chance_pct', 'label' => 'Thêm dòng (%)', 'type' => 'num'], ['name' => 'extra_lines_min', 'label' => 'Dòng thêm tối thiểu', 'type' => 'int'],
                ['name' => 'extra_lines_max', 'label' => 'Dòng thêm tối đa', 'type' => 'int'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
            ],
            'validate' => function (PDO $pdo) {
                if ((int)$pdo->query('SELECT COUNT(*) FROM craft_recipe WHERE success_pct > 100 OR success_pct < 0 OR boost_success_pct > 100 OR extra_line_chance_pct > 100 OR extra_lines_min > extra_lines_max')->fetchColumn() > 0) {
                    throw new RuntimeException('Tỉ lệ phải từ 0 đến 100 và số dòng thêm tối thiểu không được lớn hơn tối đa.');
                }
            },
        ],
        'recipeinputs' => [
            'table' => 'craft_recipe_input', 'title' => 'Nguyên liệu ghép', 'domain' => 'SUPPORT', 'pk' => ['recipe_id', 'material_template_id'], 'has_updated_by' => true,
            'intro' => 'Số lượng cần cho mỗi lần thử và phần bị mất khi thất bại (%). Thành công thì mất toàn bộ số lượng.',
            'cols' => [
                ['name' => 'recipe_id', 'label' => 'Công thức', 'type' => 'ro'], ['name' => 'material_template_id', 'label' => 'Nguyên liệu (mã)', 'type' => 'ro'],
                ['name' => 'quantity', 'label' => 'Số lượng', 'type' => 'int'], ['name' => 'lose_on_fail_pct', 'label' => 'Mất khi thất bại (%)', 'type' => 'num'],
            ],
        ],
    ];
    $specs['bookpool']['insert'] = [
        ['name' => 'stat_key', 'label' => 'Chỉ số', 'type' => 'select', 'options' => lookup($pdo, 'SELECT stat_key, name_vi FROM stat_type ORDER BY sort_order')],
        ['name' => 'min_value', 'label' => 'Nhỏ nhất', 'type' => 'num'], ['name' => 'max_value', 'label' => 'Lớn nhất', 'type' => 'num'],
        ['name' => 'weight', 'label' => 'Trọng số', 'type' => 'int', 'default' => '1'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'],
    ];
    return $specs;
}

register_grids('item', 'grids_item', [
    'bookpool' => 'Sách tuyệt kỹ',
    'recipes' => 'Công thức ghép',
    'recipeinputs' => 'Nguyên liệu ghép',
]);
