<?php
// BattleBall admin: helpers shared by the screens.
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
