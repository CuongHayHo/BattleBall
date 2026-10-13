<?php
// BattleBall admin: grid screens of the group "Hệ thống".
declare(strict_types=1);

/** Grid screens of the group "Hệ thống" (spec format: see grid_page() in lib.php). */
function grids_system(PDO $pdo): array
{
    $specs = [
        'balance' => [
            'table' => 'balance_config', 'title' => 'Tham số cân bằng', 'domain' => 'BALANCE', 'pk' => ['config_key'], 'has_updated_by' => true,
            'intro' => 'Giá trị là JSON hợp lệ. Cẩn thận: đổi ở đây ảnh hưởng toàn bộ trò chơi sau khi server nạp lại.',
            'cols' => [
                ['name' => 'config_key', 'label' => 'Khóa', 'type' => 'ro'], ['name' => 'value_json', 'label' => 'Giá trị (JSON)', 'type' => 'json'],
                ['name' => 'description', 'label' => 'Mô tả', 'type' => 'text', 'null' => true],
            ],
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
    ];
    return $specs;
}

register_grids('system', 'grids_system', [
    'balance' => 'Cân bằng',
    'runs' => 'Các trận',
    'anomalies' => 'Bất thường',
]);
