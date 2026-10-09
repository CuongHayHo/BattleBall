<?php
// BattleBall admin: shared helpers (config, database, session, CSRF, audit log, version bump, grid editor, layout).
// Every change goes through a transaction, writes admin_audit_log and bumps static_data_version for its domain.
declare(strict_types=1);

function cfg(string $key, $default = null)
{
    static $c = null;
    if ($c === null) {
        $c = [
            'db_host' => getenv('BB_DB_HOST') ?: '127.0.0.1',
            'db_port' => getenv('BB_DB_PORT') ?: '3306',
            'db_name' => getenv('BB_DB_NAME') ?: 'battleball',
            'db_user' => getenv('BB_DB_USER') ?: 'root',
            'db_pass' => getenv('BB_DB_PASS') !== false ? getenv('BB_DB_PASS') : '',
            // The page is reachable only from these addresses (comma separated in BB_ADMIN_IPS). Default: this machine.
            'allowed_ips' => array_filter(array_map('trim', explode(',', getenv('BB_ADMIN_IPS') ?: '127.0.0.1,::1'))),
        ];
        $file = __DIR__ . '/config.php';
        if (is_file($file)) {
            $c = array_replace($c, require $file);
        }
    }
    return $c[$key] ?? $default;
}

function db(): PDO
{
    static $pdo = null;
    if ($pdo === null) {
        $dsn = sprintf('mysql:host=%s;port=%s;dbname=%s;charset=utf8mb4', cfg('db_host'), cfg('db_port'), cfg('db_name'));
        $pdo = new PDO($dsn, cfg('db_user'), cfg('db_pass'), [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
        ]);
    }
    return $pdo;
}

function h($s): string
{
    return htmlspecialchars((string)$s, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

function boot(): void
{
    if (!in_array($_SERVER['REMOTE_ADDR'] ?? '', cfg('allowed_ips'), true)) {
        http_response_code(403);
        exit('Forbidden');
    }
    session_name('bbadmin');
    session_set_cookie_params(['httponly' => true, 'samesite' => 'Strict', 'path' => '/']);
    session_start();
    header('X-Frame-Options: DENY');
    header('Referrer-Policy: no-referrer');
    header("Content-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'");
}

function csrf_token(): string
{
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(32));
    }
    return $_SESSION['csrf'];
}

function csrf_field(): string
{
    return '<input type="hidden" name="_csrf" value="' . h(csrf_token()) . '">';
}

function csrf_check(): void
{
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $sent = (string)($_POST['_csrf'] ?? '');
        if (!hash_equals(csrf_token(), $sent)) {
            http_response_code(400);
            exit('Invalid CSRF token');
        }
    }
}

function admin_name(): ?string
{
    return $_SESSION['admin'] ?? null;
}

function flash(string $msg, string $kind = 'ok'): void
{
    $_SESSION['flash'][] = [$kind, $msg];
}

function redirect(string $url): void
{
    header('Location: ' . $url);
    exit;
}

function audit(PDO $pdo, string $table, string $rowKey, string $action, ?array $before, ?array $after): void
{
    $st = $pdo->prepare('INSERT INTO admin_audit_log (admin_user, table_name, row_key, action, before_json, after_json) VALUES (?,?,?,?,?,?)');
    $st->execute([
        admin_name() ?? '?', $table, $rowKey, $action,
        $before === null ? null : json_encode($before, JSON_UNESCAPED_UNICODE),
        $after === null ? null : json_encode($after, JSON_UNESCAPED_UNICODE),
    ]);
}

// static_data_version tells the game server and the clients to reload a domain: STAT, SKIN, SKILL, BALANCE, SUPPORT, ACHIEVEMENT.
function bump(PDO $pdo, string $domain): void
{
    $st = $pdo->prepare('INSERT INTO static_data_version (domain, version, updated_by) VALUES (?,1,?) ON DUPLICATE KEY UPDATE version = version + 1, updated_by = VALUES(updated_by)');
    $st->execute([$domain, admin_name()]);
}

/** Runs $fn inside a transaction; on any error rolls back and shows the message. Returns true when committed. */
function in_tx(PDO $pdo, callable $fn): bool
{
    try {
        $pdo->beginTransaction();
        $fn();
        $pdo->commit();
        return true;
    } catch (Throwable $e) {
        if ($pdo->inTransaction()) {
            $pdo->rollBack();
        }
        $msg = $e instanceof PDOException ? 'Lỗi cơ sở dữ liệu: ' . $e->getMessage() : $e->getMessage();
        flash($msg, 'err');
        return false;
    }
}

function same_value($a, $b, string $type): bool
{
    if ($a === null || $b === null) {
        return $a === $b;
    }
    if (in_array($type, ['bool', 'int', 'num'], true)) {
        return abs((float)$a - (float)$b) < 1e-9;
    }
    return (string)$a === (string)$b;
}

/** Reads one posted value according to the column type. Throws RuntimeException on invalid input. */
function read_value(array $col, array $src)
{
    $name = $col['name'];
    $type = $col['type'];
    $label = $col['label'] ?? $name;
    if ($type === 'bool') {
        return isset($src[$name]) ? 1 : 0;
    }
    $raw = isset($src[$name]) ? trim((string)$src[$name]) : '';
    if ($raw === '') {
        if (!empty($col['null'])) {
            return null;
        }
        throw new RuntimeException("«{$label}» không được để trống.");
    }
    switch ($type) {
        case 'int':
            if (filter_var($raw, FILTER_VALIDATE_INT) === false || (int)$raw < 0) {
                throw new RuntimeException("«{$label}» phải là số nguyên không âm.");
            }
            return (int)$raw;
        case 'num':
            if (!is_numeric($raw)) {
                throw new RuntimeException("«{$label}» phải là số.");
            }
            return $raw;
        case 'select':
            if (!array_key_exists($raw, $col['options'])) {
                throw new RuntimeException("«{$label}»: giá trị không hợp lệ.");
            }
            return $raw;
        case 'json':
            json_decode($raw);
            if (json_last_error() !== JSON_ERROR_NONE) {
                throw new RuntimeException("«{$label}» không phải JSON hợp lệ.");
            }
            return $raw;
        default:
            if (!empty($col['pattern']) && !preg_match($col['pattern'], $raw)) {
                throw new RuntimeException("«{$label}» không đúng định dạng" . (!empty($col['hint']) ? ' (' . $col['hint'] . ')' : '') . '.');
            }
            return mb_substr($raw, 0, (int)($col['max'] ?? 255));
    }
}

function render_input(array $col, string $nameAttr, $value): string
{
    $type = $col['type'];
    $ro = $type === 'ro';
    $val = $value === null ? '' : (string)$value;
    if ($ro) {
        return '<span>' . h($val) . '</span>';
    }
    if ($type === 'bool') {
        return '<input type="checkbox" name="' . h($nameAttr) . '" value="1"' . ($val === '1' ? ' checked' : '') . '>';
    }
    if ($type === 'select') {
        $o = '<select name="' . h($nameAttr) . '">';
        foreach ($col['options'] as $k => $lab) {
            $o .= '<option value="' . h((string)$k) . '"' . ((string)$k === $val ? ' selected' : '') . '>' . h($lab) . '</option>';
        }
        return $o . '</select>';
    }
    $cls = in_array($type, ['int', 'num'], true) ? ' class="num"' : '';
    if ($type === 'json') {
        return '<textarea name="' . h($nameAttr) . '" rows="2" class="wide">' . h($val) . '</textarea>';
    }
    return '<input type="text" name="' . h($nameAttr) . '" value="' . h($val) . '"' . $cls . '>';
}

/**
 * Table editor. $spec: table, title, intro, domain, pk[], cols[ [name,label,type,(null),(options),(max)] ], order, has_updated_by, where.
 * Edits existing rows; when $spec['insert'] (list of columns, key columns included) is set, a form adds new rows.
 */
function grid_page(PDO $pdo, array $spec, string $page): void
{
    $table = $spec['table'];
    $pk = $spec['pk'];
    $cols = $spec['cols'];
    $names = array_map(fn($c) => $c['name'], $cols);
    $select = 'SELECT ' . implode(',', array_map(fn($n) => "`$n`", $names)) . ' FROM `' . $table . '`'
        . (!empty($spec['where']) ? ' WHERE ' . $spec['where'] : '')
        . ' ORDER BY ' . ($spec['order'] ?? implode(',', array_map(fn($n) => "`$n`", $pk)));

    if (!empty($spec['select_sql'])) {
        $select = $spec['select_sql'];   // joined listing; the select must include the key columns
    }

    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['_action'] ?? '') === 'insert' && !empty($spec['insert'])) {
        $ok = in_tx($pdo, function () use ($pdo, $spec, $table, $pk) {
            $vals = [];
            foreach ($spec['insert'] as $c) {
                $vals[$c['name']] = read_value($c, $_POST['new'] ?? []);
            }
            $ik = $spec['insert_key'] ?? $pk;   // columns that identify a row when the primary key is an auto-increment id
            $where = implode(' AND ', array_map(fn($k) => "`$k` <=> ?", $ik));   // null-safe equality (dungeon_id may be NULL)
            $dup = $pdo->prepare("SELECT COUNT(*) FROM `$table` WHERE $where");
            $dup->execute(array_map(fn($k) => $vals[$k], $ik));
            if ((int)$dup->fetchColumn() > 0) {
                throw new RuntimeException('Dòng này đã tồn tại.');
            }
            $names = array_keys($vals);
            $params = array_values($vals);
            if (!empty($spec['has_updated_by'])) {
                $names[] = 'updated_by';
                $params[] = admin_name();
            }
            $sql = 'INSERT INTO `' . $table . '` (' . implode(',', array_map(fn($n) => "`$n`", $names)) . ') VALUES (' . implode(',', array_fill(0, count($names), '?')) . ')';
            $pdo->prepare($sql)->execute($params);
            audit($pdo, $table, implode(':', array_map(fn($k) => $vals[$k], $ik)), 'INSERT', null, $vals);
            if (!empty($spec['validate'])) {
                ($spec['validate'])($pdo);
            }
            if (!empty($spec['domain'])) {
                bump($pdo, $spec['domain']);
            }
            flash('Đã thêm dòng mới.');
        });
        redirect('?p=' . urlencode($page));
    }

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $changed = 0;
        $ok = in_tx($pdo, function () use ($pdo, $spec, $cols, $pk, $table, &$changed) {
            foreach (($_POST['rows'] ?? []) as $r) {
                $keyVals = json_decode((string)($r['_pk'] ?? ''), true);
                if (!is_array($keyVals) || count($keyVals) !== count($pk)) {
                    throw new RuntimeException('Dòng gửi lên không hợp lệ.');
                }
                $where = implode(' AND ', array_map(fn($k) => "`$k` = ?", $pk));
                $st = $pdo->prepare("SELECT * FROM `$table` WHERE $where");
                $st->execute(array_values($keyVals));
                $old = $st->fetch();
                if (!$old) {
                    throw new RuntimeException('Không tìm thấy dòng cần sửa.');
                }
                $before = [];
                $after = [];
                foreach ($cols as $c) {
                    if ($c['type'] === 'ro') {
                        continue;
                    }
                    $new = read_value($c, $r);
                    if (!same_value($old[$c['name']], $new, $c['type'])) {
                        $before[$c['name']] = $old[$c['name']];
                        $after[$c['name']] = $new;
                    }
                }
                if ($after) {
                    $set = implode(', ', array_map(fn($n) => "`$n` = ?", array_keys($after)));
                    $params = array_values($after);
                    if (!empty($spec['has_updated_by'])) {
                        $set .= ', `updated_by` = ?';
                        $params[] = admin_name();
                    }
                    $up = $pdo->prepare("UPDATE `$table` SET $set WHERE $where");
                    $up->execute(array_merge($params, array_values($keyVals)));
                    audit($pdo, $table, implode(':', $keyVals), 'UPDATE', $before, $after);
                    $changed++;
                }
            }
            if (!empty($spec['validate'])) {
                ($spec['validate'])($pdo);   // throws RuntimeException to reject the whole save
            }
            if ($changed && !empty($spec['domain'])) {
                bump($pdo, $spec['domain']);
            }
        });
        if ($ok) {
            flash($changed ? "Đã lưu $changed dòng." : 'Không có thay đổi.');
        }
        redirect('?p=' . urlencode($page));
    }

    $rows = $pdo->query($select)->fetchAll();
    layout_start($spec['title'], $page);
    echo '<h1>' . h($spec['title']) . '</h1>';
    if (!empty($spec['intro'])) {
        echo '<p class="muted">' . h($spec['intro']) . '</p>';
    }
    echo '<form method="post" action="?p=' . h($page) . '">' . csrf_field() . '<div class="scroll"><table><thead><tr>';
    foreach ($cols as $c) {
        echo '<th>' . h($c['label'] ?? $c['name']) . '</th>';
    }
    echo '</tr></thead><tbody>';
    foreach ($rows as $i => $row) {
        $keyVals = [];
        foreach ($pk as $k) {
            $keyVals[] = $row[$k];
        }
        echo '<tr><td style="display:none"><input type="hidden" name="rows[' . $i . '][_pk]" value="' . h(json_encode($keyVals)) . '"></td>';
        foreach ($cols as $c) {
            echo '<td>' . render_input($c, 'rows[' . $i . '][' . $c['name'] . ']', $row[$c['name']]) . '</td>';
        }
        echo '</tr>';
    }
    echo '</tbody></table></div><p><button type="submit">Lưu thay đổi</button> <span class="muted">' . count($rows) . ' dòng</span></p></form>';
    if (!empty($spec['insert'])) {
        echo '<h2>Thêm dòng mới</h2><form method="post" action="?p=' . h($page) . '">' . csrf_field() . '<input type="hidden" name="_action" value="insert"><table><tbody>';
        foreach ($spec['insert'] as $c) {
            echo field_row($c['label'] ?? $c['name'], render_input($c, 'new[' . $c['name'] . ']', $c['default'] ?? null), $c['hint'] ?? '');
        }
        echo '</tbody></table><p><button type="submit">Thêm</button></p></form>';
    }
    layout_end();
}

/**
 * Editor for "stat rows" of one owner (skin_stat / support_item_stat). Handles its own POST fields stat[key][value], stat[key][del],
 * new_stat_key, new_stat_value. Call inside a transaction. Returns number of changes. Warns (does not block) above suggested_max.
 */
function save_stat_rows(PDO $pdo, string $table, string $idCol, $id, array $post): int
{
    $changes = 0;
    $types = [];
    foreach ($pdo->query('SELECT stat_key, suggested_max FROM stat_type') as $r) {
        $types[$r['stat_key']] = $r['suggested_max'];
    }
    $cur = $pdo->prepare("SELECT stat_key, `value` FROM `$table` WHERE `$idCol` = ?");
    $cur->execute([$id]);
    foreach ($cur->fetchAll() as $row) {
        $k = $row['stat_key'];
        $in = $post['stat'][$k] ?? null;
        if ($in === null) {
            continue;
        }
        if (!empty($in['del'])) {
            $pdo->prepare("DELETE FROM `$table` WHERE `$idCol` = ? AND stat_key = ?")->execute([$id, $k]);
            audit($pdo, $table, "$id:$k", 'DELETE', ['value' => $row['value']], null);
            $changes++;
            continue;
        }
        $v = trim((string)($in['value'] ?? ''));
        if ($v === '' || !is_numeric($v)) {
            throw new RuntimeException("Giá trị chỉ số $k phải là số.");
        }
        if (abs((float)$v - (float)$row['value']) > 1e-9) {
            $pdo->prepare("UPDATE `$table` SET `value` = ?, updated_by = ? WHERE `$idCol` = ? AND stat_key = ?")->execute([$v, admin_name(), $id, $k]);
            audit($pdo, $table, "$id:$k", 'UPDATE', ['value' => $row['value']], ['value' => $v]);
            $changes++;
            if ($types[$k] !== null && (float)$v > (float)$types[$k]) {
                flash("Cảnh báo: $k = $v vượt mức gợi ý {$types[$k]}.", 'warn');
            }
        }
    }
    $nk = trim((string)($post['new_stat_key'] ?? ''));
    if ($nk !== '') {
        $nv = trim((string)($post['new_stat_value'] ?? ''));
        if (!array_key_exists($nk, $types)) {
            throw new RuntimeException('Loại chỉ số mới không tồn tại.');
        }
        if ($nv === '' || !is_numeric($nv)) {
            throw new RuntimeException('Giá trị chỉ số mới phải là số.');
        }
        $pdo->prepare("INSERT INTO `$table` (`$idCol`, stat_key, `value`, updated_by) VALUES (?,?,?,?)")->execute([$id, $nk, $nv, admin_name()]);
        audit($pdo, $table, "$id:$nk", 'INSERT', null, ['value' => $nv]);
        $changes++;
        if ($types[$nk] !== null && (float)$nv > (float)$types[$nk]) {
            flash("Cảnh báo: $nk = $nv vượt mức gợi ý {$types[$nk]}.", 'warn');
        }
    }
    return $changes;
}

function stat_rows_form(PDO $pdo, string $table, string $idCol, $id): void
{
    $st = $pdo->prepare("SELECT s.stat_key, s.`value`, t.name_vi, t.enabled, t.unit FROM `$table` s JOIN stat_type t USING (stat_key) WHERE s.`$idCol` = ? ORDER BY t.sort_order");
    $st->execute([$id]);
    echo '<h2>Chỉ số</h2><table><thead><tr><th>Chỉ số</th><th>Giá trị</th><th>Xóa</th></tr></thead><tbody>';
    foreach ($st->fetchAll() as $r) {
        $k = $r['stat_key'];
        echo '<tr><td>' . h($r['name_vi']) . ' <span class="muted">' . h($k) . '</span>' . ($r['enabled'] ? '' : ' <span class="tag off">đang tắt</span>') . '</td>'
            . '<td><input class="num" type="text" name="stat[' . h($k) . '][value]" value="' . h($r['value']) . '"></td>'
            . '<td><input type="checkbox" name="stat[' . h($k) . '][del]" value="1"></td></tr>';
    }
    echo '<tr><td><select name="new_stat_key"><option value="">+ thêm chỉ số...</option>';
    foreach ($pdo->query('SELECT stat_key, name_vi, enabled FROM stat_type ORDER BY sort_order') as $t) {
        echo '<option value="' . h($t['stat_key']) . '">' . h($t['name_vi'] . ($t['enabled'] ? '' : ' (đang tắt)')) . '</option>';
    }
    echo '</select></td><td><input class="num" type="text" name="new_stat_value" placeholder="giá trị"></td><td></td></tr></tbody></table>';
}

function layout_start(string $title, string $active = ''): void
{
    $menu = [
        'home' => 'Tổng quan', 'stats' => 'Chỉ số', 'skins' => 'Skin', 'kinds' => 'Loại skin', 'rarity' => 'Độ hiếm', 'groups' => 'Giá theo nhóm',
        'supports' => 'Đồ hỗ trợ', 'bookpool' => 'Sách tuyệt kỹ', 'dungeons' => 'Phó bản', 'monsters' => 'Quái phó bản', 'monsterstats' => 'Chỉ số quái', 'bossmult' => 'Hệ số boss', 'droprules' => 'Bảng rơi đồ', 'gemrewards' => 'Nguồn đá quý', 'dropmult' => 'Hệ số rơi phó bản', 'dungeonbosses' => 'Boss phó bản', 'bossgroups' => 'Nhóm boss', 'recipes' => 'Công thức ghép', 'recipeinputs' => 'Nguyên liệu ghép', 'achievements' => 'Thành tích', 'defaultskins' => 'Avatar mặc định',
        'mail' => 'Hòm thư', 'notices' => 'Thông báo', 'balance' => 'Cân bằng', 'boss_group_picker.html' => 'Nhóm boss phó bản', 'drop_picker.html' => 'Chọn boss rơi', 'runs' => 'Các trận', 'anomalies' => 'Bất thường', 'audit' => 'Nhật ký',
    ];
    echo '<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        . '<title>' . h($title) . ' · BattleBall Admin</title><link rel="stylesheet" href="style.css"></head><body>';
    if (admin_name() !== null) {
        echo '<nav>';
        foreach ($menu as $k => $lab) {
            echo '<a href="' . (substr($k, -5) === '.html' ? h($k) : '?p=' . $k) . '"' . ($k === $active ? ' class="on"' : '') . '>' . h($lab) . '</a>';
        }
        echo '<span class="sp"></span><span class="muted">' . h(admin_name()) . '</span>'
            . '<form method="post" action="?p=logout" class="inline">' . csrf_field() . '<button type="submit" class="link">Đăng xuất</button></form></nav>';
    }
    echo '<main>';
    foreach (($_SESSION['flash'] ?? []) as [$kind, $msg]) {
        echo '<div class="flash ' . h($kind) . '">' . h($msg) . '</div>';
    }
    unset($_SESSION['flash']);
}

function layout_end(): void
{
    echo '</main></body></html>';
}
