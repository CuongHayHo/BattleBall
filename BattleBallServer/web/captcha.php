<?php
// The captcha picture (SVG). A new text is made each time the picture is requested; the answer is kept in the session.
declare(strict_types=1);
require __DIR__ . '/lib.php';
start_session();
header('Content-Type: image/svg+xml; charset=utf-8');
header('Cache-Control: no-store');
echo captcha_svg(new_captcha());
