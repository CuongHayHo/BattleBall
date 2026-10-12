<?php
// BattleBall web registration: shared helpers. The rules MUST stay identical to the game server
// (server/src/main/java/com/battleball/server/auth/AccountRules.java and AccountRepository.createAccount): both doors write the same rows.
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

function start_session(): void
{
    session_name('bbweb');
    session_set_cookie_params(['httponly' => true, 'samesite' => 'Strict', 'path' => '/']);
    session_start();
    header('X-Frame-Options: DENY');
    header('Referrer-Policy: no-referrer');
    header("Content-Security-Policy: default-src 'self'; img-src 'self'; style-src 'self'");
}

function csrf_token(): string
{
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(32));
    }
    return $_SESSION['csrf'];
}

function client_ip(): string
{
    // Behind a reverse proxy set BB_TRUST_PROXY=1 so that the real address is used for the rate limit.
    if (getenv('BB_TRUST_PROXY') === '1' && !empty($_SERVER['HTTP_X_FORWARDED_FOR'])) {
        return trim(explode(',', $_SERVER['HTTP_X_FORWARDED_FOR'])[0]);
    }
    return $_SERVER['REMOTE_ADDR'] ?? '0.0.0.0';
}

// ---- account rules (same as the game server) ----
function valid_username(string $u): bool
{
    return (bool)preg_match('/^[a-z0-9_]{4,20}$/', $u);
}

function valid_password(string $p): bool
{
    $n = strlen($p);   // bytes, like the server (bcrypt reads at most 72 bytes)
    return $n >= 8 && $n <= 64;
}

// ---- captcha: a distorted text drawn as SVG (this PHP has no GD) ----
function new_captcha(): string
{
    $alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
    $text = '';
    for ($i = 0; $i < 5; $i++) {
        $text .= $alphabet[random_int(0, strlen($alphabet) - 1)];
    }
    if (getenv('BB_WEB_TEST_CAPTCHA') === '1') {
        $text = 'TEST1';   // tests only; never set this on a real server
    }
    $_SESSION['captcha'] = $text;
    $_SESSION['captcha_at'] = time();
    return $text;
}

function check_captcha(string $answer): bool
{
    $ok = isset($_SESSION['captcha'], $_SESSION['captcha_at']) && time() - $_SESSION['captcha_at'] < 600
        && hash_equals((string)$_SESSION['captcha'], strtoupper(trim($answer)));
    unset($_SESSION['captcha'], $_SESSION['captcha_at']);   // one try per picture
    return $ok;
}

function captcha_svg(string $text): string
{
    $w = 190;
    $h = 56;
    $out = '<svg xmlns="http://www.w3.org/2000/svg" width="' . $w . '" height="' . $h . '" viewBox="0 0 ' . $w . ' ' . $h . '"><rect width="100%" height="100%" fill="#f2f4f8"/>';
    for ($i = 0; $i < 7; $i++) {
        $out .= sprintf('<path d="M%d %d Q%d %d %d %d" stroke="#%s" stroke-width="1.5" fill="none"/>', random_int(0, $w), random_int(0, $h), random_int(0, $w), random_int(0, $h),
            random_int(0, $w), random_int(0, $h), dechex(random_int(0x555555, 0xbbbbbb)));
    }
    $chars = str_split($text);
    foreach ($chars as $i => $ch) {
        $x = 18 + $i * 34 + random_int(-3, 3);
        $y = 38 + random_int(-5, 5);
        $out .= sprintf('<text x="%d" y="%d" font-family="monospace" font-size="%d" font-weight="bold" fill="#%s" transform="rotate(%d %d %d) skewX(%d)">%s</text>',
            $x, $y, random_int(28, 36), dechex(random_int(0x112244, 0x446688)), random_int(-22, 22), $x, $y, random_int(-12, 12), htmlspecialchars($ch));
    }
    for ($i = 0; $i < 40; $i++) {
        $out .= sprintf('<circle cx="%d" cy="%d" r="1" fill="#888"/>', random_int(0, $w), random_int(0, $h));
    }
    return $out . '</svg>';
}

// ---- rate limit by IP (table web_register_attempt, migration V023) ----
function rate_limited(PDO $pdo, string $ip): bool
{
    $st = $pdo->prepare('SELECT COUNT(*) FROM web_register_attempt WHERE ip = ? AND created_at > (NOW() - INTERVAL 1 HOUR)');
    $st->execute([$ip]);
    if ((int)$st->fetchColumn() >= 10) {
        return true;       // at most 10 attempts per hour
    }
    $st = $pdo->prepare('SELECT COUNT(*) FROM web_register_attempt WHERE ip = ? AND success = 1 AND created_at > (NOW() - INTERVAL 1 DAY)');
    $st->execute([$ip]);
    return (int)$st->fetchColumn() >= 5;   // at most 5 new accounts per day
}

function record_attempt(PDO $pdo, string $ip, bool $success): void
{
    $pdo->prepare('INSERT INTO web_register_attempt (ip, success) VALUES (?, ?)')->execute([$ip, $success ? 1 : 0]);
}

/**
 * Creates the account with its 3 characters, wallets, the basic punch skill, the starter skins and the equipped starter skin, in ONE transaction
 * (the same rows as AccountRepository.createAccount of the game server). Returns 'ok', 'taken' or throws.
 */
function create_account(PDO $pdo, string $username, string $password): string
{
    $hash = password_hash($password, PASSWORD_BCRYPT, ['cost' => 12]);   // $2y$ hash: the Java server verifies it
    $pdo->beginTransaction();
    try {
        $pdo->prepare("INSERT INTO account (username, password_hash) VALUES (?, ?)")->execute([$username, $hash]);
        $accountId = (int)$pdo->lastInsertId();
        // the three characters share one wallet and one set of owned skins (V026)
        $pdo->prepare('INSERT INTO account_wallet (account_id) VALUES (?)')->execute([$accountId]);
        // welcome mail (V047): while the admin switch is on, one mail with the gift lines of welcome_gift (same rows as AccountRepository.sendWelcomeMail)
        $mail = $pdo->prepare("INSERT INTO mail (account_id, title, body, sent_by) SELECT ?, title, body, 'system' FROM welcome_mail_setting WHERE setting_id = 1 AND enabled = 1");
        $mail->execute([$accountId]);
        if ($mail->rowCount() === 1) {
            $pdo->prepare('INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT ?, line_no, kind, ref_id, quantity FROM welcome_gift')->execute([(int)$pdo->lastInsertId()]);
        }
        // campaign mails (V050): one mail for every campaign that is on, with its gift lines (same rows as AccountRepository.sendCampaignMails)
        $campaigns = $pdo->query('SELECT campaign_id FROM signup_campaign WHERE enabled = 1 AND ends_at > NOW() ORDER BY campaign_id')->fetchAll(PDO::FETCH_COLUMN);
        foreach ($campaigns as $campaignId) {
            $pdo->prepare("INSERT INTO mail (account_id, title, body, sent_by, expires_at) SELECT ?, title, body, CONCAT('campaign:', campaign_id), ends_at FROM signup_campaign WHERE campaign_id = ?")->execute([$accountId, (int)$campaignId]);
            $pdo->prepare('INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT ?, line_no, kind, ref_id, quantity FROM signup_campaign_gift WHERE campaign_id = ?')->execute([(int)$pdo->lastInsertId(), (int)$campaignId]);
        }
        $pdo->prepare("INSERT IGNORE INTO owned_skin (account_id, costume_id, source) SELECT ?, costume_id, 'DEFAULT' FROM race_default_skin")->execute([$accountId]);
        foreach (['EARTH', 'NAMEC', 'XAYDA'] as $race) {
            $pdo->prepare('INSERT INTO game_character (account_id, race) VALUES (?, ?)')->execute([$accountId, $race]);
            $cid = (int)$pdo->lastInsertId();
            $pdo->prepare('INSERT INTO character_skill (character_id, skill_id, `level`) SELECT ?, skill_id, 1 FROM skill_template WHERE race = ? AND slot = 0')->execute([$cid, $race]);
            $pdo->prepare("INSERT INTO character_equipment (character_id, slot, costume_id) SELECT ?, 'SKIN', costume_id FROM race_default_skin WHERE race = ? AND is_equipped = 1")->execute([$cid, $race]);
        }
        $pdo->commit();
        return 'ok';
    } catch (PDOException $e) {
        $pdo->rollBack();
        if (($e->errorInfo[1] ?? 0) === 1062) {
            return 'taken';
        }
        throw $e;
    }
}
