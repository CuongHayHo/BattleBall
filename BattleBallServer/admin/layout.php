<?php
// BattleBall admin: the page frame. A sidebar menu built from registry.php (groups fold open on the active one; on a phone it slides in
// from a checkbox, no script), then <main> for the screen. Without a login only the plain centered box is drawn.
declare(strict_types=1);

function menu_href(array $it): string
{
    return isset($it['href']) ? h($it['href']) : '?p=' . h($it['key']);
}

function layout_start(string $title, string $active = ''): void
{
    $hidden = registry()['hidden'];
    if (isset($hidden[$active])) {
        $active = $hidden[$active]['parent'];
    }
    $in = admin_name() !== null;
    $GLOBALS['bb_shell'] = $in;
    echo '<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        . '<title>' . h($title) . ' · BattleBall Admin</title><link rel="stylesheet" href="style.css"></head><body class="' . ($in ? 'app' : 'auth') . '">';
    if ($in) {
        echo '<input type="checkbox" id="navtoggle" class="navtoggle" aria-label="Menu">'
            . '<header class="topbar"><label for="navtoggle" class="burger" aria-hidden="true"><i></i><i></i><i></i></label><span class="brand"><span class="ball"></span><span>BattleBall <b>Admin</b></span></span></header>'
            . '<div class="shell"><label for="navtoggle" class="scrim"></label><aside class="side"><div class="brand"><span class="ball"></span><span>BattleBall <b>Admin</b></span></div><nav>';
        foreach (menu_tree() as $g => $grp) {
            if ($grp['title'] === '') {
                foreach ($grp['items'] as $it) {
                    echo '<a class="top' . ($it['key'] === $active ? ' on' : '') . '" href="' . menu_href($it) . '">' . h($it['label']) . '</a>';
                }
                continue;
            }
            $open = false;
            foreach ($grp['items'] as $it) {
                $open = $open || $it['key'] === $active;
            }
            echo '<details class="grp"' . ($open ? ' open' : '') . '><summary>' . h($grp['title']) . '</summary>';
            foreach ($grp['items'] as $it) {
                echo '<a href="' . menu_href($it) . '"' . ($it['key'] === $active ? ' class="on"' : '') . '>' . h($it['label']) . '</a>';
            }
            echo '</details>';
        }
        echo '</nav><div class="who"><span>' . h(admin_name()) . '</span>'
            . '<form method="post" action="?p=logout" class="inline">' . csrf_field() . '<button type="submit" class="link">Đăng xuất</button></form></div></aside>';
    }
    echo '<main>';
    foreach (($_SESSION['flash'] ?? []) as [$kind, $msg]) {
        echo '<div class="flash ' . h($kind) . '">' . h($msg) . '</div>';
    }
    unset($_SESSION['flash']);
}

function layout_end(): void
{
    echo '</main>' . (!empty($GLOBALS['bb_shell']) ? '</div>' : '') . '</body></html>';
}
