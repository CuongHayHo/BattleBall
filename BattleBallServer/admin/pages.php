<?php
// BattleBall admin: screens. See index.php for routing and lib.php for helpers.
declare(strict_types=1);

function lookup(PDO $pdo, string $sql): array
{
    $out = [];
    foreach ($pdo->query($sql)->fetchAll(PDO::FETCH_NUM) as $r) {
        $out[$r[0]] = $r[1];
    }
    return $out;
}

/** Updates the given columns of one row from $src; returns [before, after] arrays of the changed columns (empty when nothing changed). */
function update_cols(PDO $pdo, string $table, string $pkCol, $id, array $cols, array $src, bool $hasUpdatedBy = true): array
{
    $st = $pdo->prepare("SELECT * FROM `$table` WHERE `$pkCol` = ?");
    $st->execute([$id]);
    $old = $st->fetch();
    if (!$old) {
        throw new RuntimeException("Không tìm thấy dòng trong $table.");
    }
    $before = [];
    $after = [];
    foreach ($cols as $c) {
        $new = read_value($c, $src);
        if (!same_value($old[$c['name']], $new, $c['type'])) {
            $before[$c['name']] = $old[$c['name']];
            $after[$c['name']] = $new;
        }
    }
    if ($after) {
        $set = implode(', ', array_map(fn($n) => "`$n` = ?", array_keys($after)));
        $params = array_values($after);
        if ($hasUpdatedBy) {
            $set .= ', `updated_by` = ?';
            $params[] = admin_name();
        }
        $pdo->prepare("UPDATE `$table` SET $set WHERE `$pkCol` = ?")->execute(array_merge($params, [$id]));
        audit($pdo, $table, (string)$id, 'UPDATE', $before, $after);
    }
    return [$before, $after];
}

function field_row(string $label, string $input, string $hint = ''): string
{
    return '<tr><th class="lab">' . h($label) . '</th><td>' . $input . ($hint !== '' ? ' <span class="muted">' . h($hint) . '</span>' : '') . '</td></tr>';
}

// ---------------------------------------------------------------------------------------------------------------- login
function page_login(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $u = trim((string)($_POST['username'] ?? ''));
        $pw = (string)($_POST['password'] ?? '');
        $st = $pdo->prepare("SELECT account_id, username, password_hash FROM account WHERE username = ? AND role = 'ADMIN' AND banned = 0");
        $st->execute([$u]);
        $row = $st->fetch();
        if ($row && password_verify($pw, $row['password_hash'])) {
            session_regenerate_id(true);
            $_SESSION['admin'] = $row['username'];
            $pdo->prepare('UPDATE account SET last_login_at = NOW() WHERE account_id = ?')->execute([$row['account_id']]);
            redirect('?p=home');
        }
        usleep(700000);   // slow down guessing
        flash('Sai tên đăng nhập hoặc mật khẩu.', 'err');
        redirect('?p=login');
    }
    layout_start('Đăng nhập');
    echo '<h1>BattleBall Admin</h1><form method="post" action="?p=login" class="card">' . csrf_field()
        . '<label>Tên đăng nhập<input type="text" name="username" autocomplete="username" required></label>'
        . '<label>Mật khẩu<input type="password" name="password" autocomplete="current-password" required></label>'
        . '<button type="submit">Đăng nhập</button></form>';
    layout_end();
}

function page_logout(PDO $pdo): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $_SESSION = [];
        session_destroy();
    }
    redirect('?p=login');
}

// ---------------------------------------------------------------------------------------------------------------- home
function page_home(PDO $pdo): void
{
    layout_start('Tổng quan', 'home');
    echo '<h1>Tổng quan</h1><h2>Phiên bản dữ liệu</h2><table><thead><tr><th>Nhóm</th><th>Phiên bản</th><th>Cập nhật</th><th>Bởi</th></tr></thead><tbody>';
    foreach ($pdo->query('SELECT domain, version, updated_at, updated_by FROM static_data_version ORDER BY domain') as $r) {
        echo '<tr><td>' . h($r['domain']) . '</td><td>' . h($r['version']) . '</td><td>' . h($r['updated_at']) . '</td><td>' . h($r['updated_by']) . '</td></tr>';
    }
    echo '</tbody></table><h2>Số lượng</h2><table><tbody>';
    $counts = [
        'Skin' => 'SELECT COUNT(*) FROM skin_template', 'Skin đang bật' => 'SELECT COUNT(*) FROM skin_template WHERE enabled = 1',
        'Loại chỉ số đang bật' => 'SELECT COUNT(*) FROM stat_type WHERE enabled = 1', 'Loại chỉ số đang tắt' => 'SELECT COUNT(*) FROM stat_type WHERE enabled = 0',
        'Đồ hỗ trợ' => 'SELECT COUNT(*) FROM support_item_template', 'Thành tích' => 'SELECT COUNT(*) FROM achievement_template',
        'Tài khoản' => 'SELECT COUNT(*) FROM account', 'Nhân vật' => 'SELECT COUNT(*) FROM game_character',
    ];
    foreach ($counts as $lab => $sql) {
        echo '<tr><th class="lab">' . h($lab) . '</th><td>' . h($pdo->query($sql)->fetchColumn()) . '</td></tr>';
    }
    echo '</tbody></table>';
    layout_end();
}

// ---------------------------------------------------------------------------------------------------------------- grids
function grid_specs(PDO $pdo): array
{
    $races = ['EARTH' => 'Trái Đất', 'NAMEC' => 'Namếc', 'XAYDA' => 'Xayda', 'ANY' => 'Mọi chủng tộc'];
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
        'achievements' => [
            'table' => 'achievement_template', 'title' => 'Thành tích (phần thưởng avatar)', 'domain' => 'ACHIEVEMENT', 'pk' => ['achievement_id'], 'has_updated_by' => true,
            'intro' => 'Avatar ngoài nhóm mặc định nhận qua thành tích. Điều kiện hiện có: đạt cấp (LEVEL_REACH), tổng số trận thắng (WINS_TOTAL), hạ boss lần đầu (FIRST_CLEAR_BOSS, ngưỡng = mã boss). Phần thưởng: mã skin và/hoặc đá quý.',
            'cols' => [
                ['name' => 'achievement_id', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120],
                ['name' => 'race', 'label' => 'Chủng tộc', 'type' => 'select', 'options' => $races],
                ['name' => 'condition_type', 'label' => 'Điều kiện', 'type' => 'select', 'options' => ['LEVEL_REACH' => 'LEVEL_REACH', 'WINS_TOTAL' => 'WINS_TOTAL', 'FIRST_CLEAR_BOSS' => 'FIRST_CLEAR_BOSS']],
                ['name' => 'threshold', 'label' => 'Ngưỡng', 'type' => 'int'], ['name' => 'reward_costume_id', 'label' => 'Skin thưởng (mã)', 'type' => 'int', 'null' => true],
                ['name' => 'reward_gems', 'label' => 'Đá quý thưởng', 'type' => 'int', 'null' => true], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool'],
            ],
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
        'runs' => [
            'table' => 'match_run', 'title' => 'Các trận gần đây', 'pk' => ['run_id'],
            'intro' => '200 trận mới nhất (chỉ xem). Trạng thái: ACTIVE đang chơi, WON thắng, LOST thua, ABANDONED bỏ giữa chừng, EXPIRED quá hạn.',
            'select_sql' => 'SELECT r.run_id, a.username, d.name_vi AS dungeon, r.status, r.started_at, r.duration_s, r.revives_used, r.gold_earned, r.gems_earned, r.reward_applied FROM match_run r JOIN account a USING (account_id) JOIN dungeon_template d USING (dungeon_id) ORDER BY r.run_id DESC LIMIT 200',
            'cols' => [
                ['name' => 'run_id', 'label' => 'Mã trận', 'type' => 'ro'], ['name' => 'username', 'label' => 'Tài khoản', 'type' => 'ro'], ['name' => 'dungeon', 'label' => 'Phó bản', 'type' => 'ro'],
                ['name' => 'status', 'label' => 'Trạng thái', 'type' => 'ro'], ['name' => 'started_at', 'label' => 'Bắt đầu', 'type' => 'ro'], ['name' => 'duration_s', 'label' => 'Thời gian (s)', 'type' => 'ro'],
                ['name' => 'revives_used', 'label' => 'Hồi sinh', 'type' => 'ro'], ['name' => 'gold_earned', 'label' => 'Vàng', 'type' => 'ro'],
                ['name' => 'gems_earned', 'label' => 'Đá quý', 'type' => 'ro'], ['name' => 'reward_applied', 'label' => 'Đã trả thưởng', 'type' => 'ro'],
            ],
        ],
        'anomalies' => [
            'table' => 'anomaly_log', 'title' => 'Sự kiện bất thường', 'pk' => ['anomaly_id'],
            'intro' => 'Chỉ ghi để xử lý thủ công, không chặn tự động. Đánh dấu "Đã xem" khi đã kiểm tra.',
            'select_sql' => 'SELECT l.anomaly_id, a.username, l.run_id, l.kind, l.detail, l.created_at, l.reviewed FROM anomaly_log l JOIN account a USING (account_id) ORDER BY l.reviewed, l.anomaly_id DESC LIMIT 300',
            'cols' => [
                ['name' => 'anomaly_id', 'label' => 'Mã', 'type' => 'ro'], ['name' => 'username', 'label' => 'Tài khoản', 'type' => 'ro'], ['name' => 'run_id', 'label' => 'Trận', 'type' => 'ro'],
                ['name' => 'kind', 'label' => 'Loại', 'type' => 'ro'], ['name' => 'detail', 'label' => 'Chi tiết', 'type' => 'ro'], ['name' => 'created_at', 'label' => 'Thời điểm', 'type' => 'ro'],
                ['name' => 'reviewed', 'label' => 'Đã xem', 'type' => 'bool'],
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
        'balance' => [
            'table' => 'balance_config', 'title' => 'Tham số cân bằng', 'domain' => 'BALANCE', 'pk' => ['config_key'], 'has_updated_by' => true,
            'intro' => 'Giá trị là JSON hợp lệ. Cẩn thận: đổi ở đây ảnh hưởng toàn bộ trò chơi sau khi server nạp lại.',
            'cols' => [
                ['name' => 'config_key', 'label' => 'Khóa', 'type' => 'ro'], ['name' => 'value_json', 'label' => 'Giá trị (JSON)', 'type' => 'json'],
                ['name' => 'description', 'label' => 'Mô tả', 'type' => 'text', 'null' => true],
            ],
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
    $specs['achievements']['insert'] = [
        ['name' => 'achievement_id', 'label' => 'Mã (số)', 'type' => 'int'], ['name' => 'name_vi', 'label' => 'Tên', 'type' => 'text', 'max' => 120],
        ['name' => 'race', 'label' => 'Chủng tộc', 'type' => 'select', 'options' => $races],
        ['name' => 'condition_type', 'label' => 'Điều kiện', 'type' => 'select', 'options' => ['LEVEL_REACH' => 'LEVEL_REACH', 'WINS_TOTAL' => 'WINS_TOTAL', 'FIRST_CLEAR_BOSS' => 'FIRST_CLEAR_BOSS']],
        ['name' => 'threshold', 'label' => 'Ngưỡng', 'type' => 'int'], ['name' => 'reward_costume_id', 'label' => 'Skin thưởng (mã)', 'type' => 'int', 'null' => true],
        ['name' => 'reward_gems', 'label' => 'Đá quý thưởng', 'type' => 'int', 'null' => true], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'],
    ];
    $specs['defaultskins']['insert'] = [
        ['name' => 'race', 'label' => 'Chủng tộc', 'type' => 'select', 'options' => ['EARTH' => 'Trái Đất', 'NAMEC' => 'Namếc', 'XAYDA' => 'Xayda']],
        ['name' => 'costume_id', 'label' => 'Mã skin (loại avatar)', 'type' => 'int'], ['name' => 'is_equipped', 'label' => 'Mặc ngay', 'type' => 'bool'],
    ];
    $specs['bookpool']['insert'] = [
        ['name' => 'stat_key', 'label' => 'Chỉ số', 'type' => 'select', 'options' => lookup($pdo, 'SELECT stat_key, name_vi FROM stat_type ORDER BY sort_order')],
        ['name' => 'min_value', 'label' => 'Nhỏ nhất', 'type' => 'num'], ['name' => 'max_value', 'label' => 'Lớn nhất', 'type' => 'num'],
        ['name' => 'weight', 'label' => 'Trọng số', 'type' => 'int', 'default' => '1'], ['name' => 'enabled', 'label' => 'Bật', 'type' => 'bool', 'default' => '1'],
    ];
    return $specs;
}

// ---------------------------------------------------------------------------------------------------------------- skins
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
    $nro = $pdo->prepare('SELECT n.stat_key, n.`value` FROM skin_nro_stat n WHERE n.costume_id = ?');
    $nro->execute([$id]);
    $list = array_map(fn($r) => $r['stat_key'] . ' ' . $r['value'], $nro->fetchAll());
    if ($list) {
        echo '<p class="muted">Chỉ số gốc trong NRO (tham khảo): ' . h(implode(', ', $list)) . '</p>';
    }
    echo '<p><button type="submit">Lưu</button></p></form>';
    layout_end();
}

// ---------------------------------------------------------------------------------------------------------------- support items
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

// ---------------------------------------------------------------------------------------------------------------- audit
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
