#!/usr/bin/env bash
# Deploys BattleBallServer to the VPS: build jar, back up the database, upload jar / migrations / web / admin
# (web/assets only when manifest.json changed), restart the service and check that port 7443 answers.
#   bash tools/deploy.sh              full deploy
#   bash tools/deploy.sh --no-build   reuse the jar already in server/target
#   bash tools/deploy.sh --assets     force re-upload of web/assets
# Settings (override with environment variables): BB_VPS_HOST, BB_VPS_USER, BB_VPS_KEY.
# The server applies new migrations by itself when it starts (BB_MIGRATE=1). Secrets stay on the VPS in /etc/battleball (never uploaded).
set -euo pipefail

HOST="${BB_VPS_HOST:-160.191.242.182}"
USER_="${BB_VPS_USER:-root}"
KEY="${BB_VPS_KEY:-$HOME/.ssh/bb_deploy}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
JAR="$ROOT/server/target/battleball-server-0.1.0-SNAPSHOT-all.jar"
SSH=(ssh -i "$KEY" -o BatchMode=yes "$USER_@$HOST")

BUILD=1; FORCE_ASSETS=0
for a in "$@"; do
  case "$a" in
    --no-build) BUILD=0 ;;
    --assets) FORCE_ASSETS=1 ;;
    *) echo "unknown option $a"; exit 2 ;;
  esac
done

if [ "$BUILD" = 1 ]; then
  echo "== build"
  powershell -NoProfile -File "$(cygpath -w "$ROOT/tools/mvn.ps1")" -B -q -f "$(cygpath -w "$ROOT/server/pom.xml")" -DskipTests package
fi
[ -f "$JAR" ] || { echo "jar not found: $JAR"; exit 1; }

echo "== backup database on the VPS"
"${SSH[@]}" 'mkdir -p /opt/battleball/backups && mariadb-dump --single-transaction --routines battleball | gzip > /opt/battleball/backups/battleball-$(date +%Y%m%d-%H%M%S).sql.gz && ls -1t /opt/battleball/backups | tail -n +11 | while read f; do rm -f "/opt/battleball/backups/$f"; done; ls -1t /opt/battleball/backups | head -1'

echo "== upload migrations, web, admin"
tar -C "$ROOT/db" -cf - migrations | "${SSH[@]}" 'tar -C /opt/battleball -xf -'
tar -C "$ROOT" --exclude='web/assets' --exclude='web/config.php' -cf - web | "${SSH[@]}" 'tar -C /opt/battleball -xf -'
tar -C "$ROOT" --exclude='admin/config.php' -cf - admin | "${SSH[@]}" 'tar -C /opt/battleball -xf -'

echo "== assets"
LOCAL_SUM=$(md5sum "$ROOT/web/assets/manifest.json" | cut -d' ' -f1)
REMOTE_SUM=$("${SSH[@]}" 'md5sum /opt/battleball/web/assets/manifest.json 2>/dev/null | cut -d" " -f1' || true)
if [ "$FORCE_ASSETS" = 1 ] || [ "$LOCAL_SUM" != "$REMOTE_SUM" ]; then
  echo "uploading web/assets (169 MB, only when manifest changed)"
  "${SSH[@]}" 'mkdir -p /opt/battleball/web/assets'
  tar -C "$ROOT/web" -cf - assets | "${SSH[@]}" 'tar -C /opt/battleball/web -xf -'
else
  echo "unchanged, skipped"
fi

echo "== jar + restart"
scp -i "$KEY" -q "$JAR" "$USER_@$HOST:/opt/battleball/server/battleball-server.jar.new"
"${SSH[@]}" 'mv /opt/battleball/server/battleball-server.jar.new /opt/battleball/server/battleball-server.jar
chown -R battleball:battleball /opt/battleball/server /opt/battleball/migrations /opt/battleball/web/assets /opt/battleball/logs
chown -R root:www-data /opt/battleball/web /opt/battleball/admin 2>/dev/null; chown -R battleball:battleball /opt/battleball/web/assets; chmod 640 /opt/battleball/web/config.php /opt/battleball/admin/config.php
systemctl restart battleball'

echo "== wait for the game port"
for i in $(seq 1 30); do
  if "${SSH[@]}" 'ss -tln | grep -q ":7443 "'; then echo "server is listening on 7443"; OK=1; break; fi
  sleep 2
done
echo "== last log lines"
"${SSH[@]}" 'tail -n 15 /opt/battleball/logs/server.log; systemctl is-active battleball'
[ "${OK:-0}" = 1 ] || { echo "SERVER DID NOT START, see log above"; exit 1; }
echo "DEPLOY OK"
