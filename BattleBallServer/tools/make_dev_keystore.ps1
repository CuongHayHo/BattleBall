# Creates a SELF-SIGNED development keystore for the game server (PKCS12, valid 825 days, localhost and 127.0.0.1).
# Production uses a real certificate (for example Let's Encrypt) in the same PKCS12 format. Usage:
#   powershell -File tools\make_dev_keystore.ps1 [-Out tools\dev-keystore.p12] [-Password changeit]
# Then set BB_TLS_KEYSTORE to the file and BB_TLS_PASSWORD to the password before starting the server.
param(
    [string]$Out = (Join-Path $PSScriptRoot 'dev-keystore.p12'),
    [string]$Password = 'changeit'
)
$keytool = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\keytool.exe' } else { 'keytool' }
if (Test-Path $Out) { Write-Host "Already exists: $Out (delete it to make a new one)"; exit 0 }
& $keytool -genkeypair -alias battleball -keyalg RSA -keysize 2048 -validity 825 -storetype PKCS12 `
    -keystore $Out -storepass $Password -dname 'CN=localhost' -ext 'SAN=dns:localhost,ip:127.0.0.1'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Created $Out"
