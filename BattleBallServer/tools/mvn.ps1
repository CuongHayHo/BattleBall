# Runs the portable Maven with the right Java. Usage: powershell -File tools\mvn.ps1 -f server\pom.xml test
# MAVEN_OPTS: the protobuf plugin opens an NIO selector inside Maven's JVM, which fails on Windows when the temp directory is an 8.3 short path
# (see server/src/main/java/com/battleball/server/PlatformFixes.java), so the Unix-domain temp directory is set to a plain path.
$root = Split-Path -Parent $PSScriptRoot
if (-not $env:JAVA_HOME) { $env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot' }
$udsTmp = Join-Path $env:USERPROFILE '.battleball\tmp'
New-Item -ItemType Directory -Force $udsTmp | Out-Null
$env:MAVEN_OPTS = "-Djdk.net.unixdomain.tmpdir=$udsTmp"
& (Join-Path $PSScriptRoot 'apache-maven-3.9.16\bin\mvn.cmd') @args
exit $LASTEXITCODE
