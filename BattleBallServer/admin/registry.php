<?php
// BattleBall admin: the list of screens. Every module in pages\ announces its screens here (register_page, register_grids, register_link,
// register_hidden) and the menu and the router are built from this list, so a new screen is one file plus one line.
declare(strict_types=1);

/** The menu groups in display order: key => title. The group 'home' has no title: its only screen is shown on top of the menu. */
const MENU_GROUPS = [
    'home' => '',
    'skin' => 'Skin & Avatar',
    'item' => 'Đồ hỗ trợ',
    'dungeon' => 'Phó bản & Quái',
    'drop' => 'Rơi đồ & Thưởng',
    'comm' => 'Thư & Thông báo',
    'system' => 'Hệ thống',
];

function &registry(): array
{
    static $r = ['items' => [], 'hidden' => []];
    return $r;
}

/** A screen with its own handler function: fn(PDO): void. $sort orders it inside the group (smaller first). */
function register_page(string $key, string $label, string $group, string $handler, int $sort = 100): void
{
    $r = &registry();
    $r['items'][$key] = ['key' => $key, 'label' => $label, 'group' => $group, 'sort' => $sort, 'handler' => $handler];
}

/** Table screens: $provider(PDO) returns the grid specs (see grid_page in lib.php); $labels = page key => menu label, shown in this order. */
function register_grids(string $group, string $provider, array $labels): void
{
    $r = &registry();
    $n = 0;
    foreach ($labels as $key => $label) {
        $n += 10;
        $r['items'][$key] = ['key' => $key, 'label' => $label, 'group' => $group, 'sort' => $n, 'provider' => $provider];
    }
}

/** A menu entry that opens a static page (for example a picker .html file). */
function register_link(string $group, string $href, string $label, int $sort = 100): void
{
    $r = &registry();
    $r['items'][$href] = ['key' => $href, 'label' => $label, 'group' => $group, 'sort' => $sort, 'href' => $href];
}

/** A screen reachable by a link or a form but not listed in the menu; $parent is the menu entry that stays highlighted. */
function register_hidden(string $key, string $handler, string $parent): void
{
    $r = &registry();
    $r['hidden'][$key] = ['key' => $key, 'handler' => $handler, 'parent' => $parent];
}

function find_route(string $key): ?array
{
    $r = &registry();
    $route = $r['items'][$key] ?? $r['hidden'][$key] ?? null;
    return $route !== null && !isset($route['href']) ? $route : null;
}

/** The menu: group key => ['title' => ..., 'items' => [entries sorted by $sort]]. Empty groups are left out. */
function menu_tree(): array
{
    $r = &registry();
    $tree = [];
    foreach (MENU_GROUPS as $g => $title) {
        $items = array_values(array_filter($r['items'], fn($it) => $it['group'] === $g));
        usort($items, fn($a, $b) => $a['sort'] <=> $b['sort']);
        if ($items) {
            $tree[$g] = ['title' => $title, 'items' => $items];
        }
    }
    return $tree;
}
