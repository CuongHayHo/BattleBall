# Starts the web server for development: the registration page and the game assets (web\assets, built by tools\build_assets.py).
#   powershell -File tools\run_web.ps1 [-Port 8081]
# Uses PHP's built-in server (XAMPP's php.exe, or any php on the PATH), bound to this computer only. Registration page: http://127.0.0.1:8081/register.php
# Assets: http://127.0.0.1:8081/assets/manifest.json. On a real server use Apache or nginx over HTTPS with the same web folder.
param([int]$Port = 8081)
$root = Split-Path -Parent $PSScriptRoot
$php = 'E:\xampp\php\php.exe'
if (-not (Test-Path $php)) { $php = (Get-Command php -ErrorAction Stop).Source }
if (-not (Test-Path (Join-Path $root 'web\assets\manifest.json'))) {
    Write-Host 'No assets built yet: running tools\build_assets.py'
    & python (Join-Path $PSScriptRoot 'build_assets.py')
}
& $php -S "127.0.0.1:$Port" -t (Join-Path $root 'web')
