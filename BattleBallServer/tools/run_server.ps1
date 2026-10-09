# Builds (if needed) and starts the game server for development.
#   powershell -File tools\run_server.ps1 [-Db battleball] [-Port 7443]
# Defaults: MariaDB on 127.0.0.1:3306 as root without password (the local XAMPP), self-signed keystore tools\dev-keystore.p12 (created on first run).
# Change the settings with the BB_* environment variables (see server\src\main\java\com\battleball\server\ServerConfig.java).
param(
    [string]$Db = 'battleball',
    [int]$Port = 7443
)
$root = Split-Path -Parent $PSScriptRoot
if (-not $env:JAVA_HOME) { $env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot' }
$jar = Join-Path $root 'server\target\battleball-server-0.1.0-SNAPSHOT-all.jar'
if (-not (Test-Path $jar)) {
    & powershell -NoProfile -File (Join-Path $PSScriptRoot 'mvn.ps1') -B -q -f (Join-Path $root 'server\pom.xml') -DskipTests package
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$keystore = if ($env:BB_TLS_KEYSTORE) { $env:BB_TLS_KEYSTORE } else { Join-Path $PSScriptRoot 'dev-keystore.p12' }
if (-not (Test-Path $keystore)) { & powershell -NoProfile -File (Join-Path $PSScriptRoot 'make_dev_keystore.ps1') -Out $keystore }
$env:BB_TLS_KEYSTORE = $keystore
if (-not $env:BB_DB_URL) { $env:BB_DB_URL = "jdbc:mariadb://127.0.0.1:3306/$Db" }
if (-not $env:BB_PORT) { $env:BB_PORT = "$Port" }
# Assets (packs built by tools/build_assets.py into webssets) are sent by this server over the game connection; BB_ASSET_BASE_URL is obsolete.
if (-not $env:BB_ASSET_MANIFEST) { $env:BB_ASSET_MANIFEST = Join-Path $root 'web\assets\manifest.json' }
& (Join-Path $env:JAVA_HOME 'bin\java.exe') -jar $jar
