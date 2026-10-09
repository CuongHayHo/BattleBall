@echo off
rem Starts everything the BattleBall dev server needs: MariaDB (XAMPP) if it is not running, the web server (assets, port 8081) and the game server (port 7443).
rem Close the two extra windows (or press Ctrl+C in them) to stop. Database: battleball on 127.0.0.1:3306, user root without password.
setlocal
cd /d "%~dp0"

rem 1) MariaDB
netstat -an | findstr /C:":3306 " | findstr LISTENING >nul
if errorlevel 1 (
    echo Starting MariaDB...
    start "BattleBall MariaDB" /min "E:\xampp\mysql\bin\mysqld.exe" --defaults-file="E:\xampp\mysql\bin\my.ini" --standalone
    timeout /t 6 /nobreak >nul
) else (
    echo MariaDB is already running.
)

rem 2) Web server for the game assets (new window)
netstat -an | findstr /C:":8081 " | findstr LISTENING >nul
if errorlevel 1 (
    start "BattleBall Web 8081" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run_web.ps1"
) else (
    echo Web server 8081 is already running.
)

rem 3) Game server (this window; applies new database migrations on start)
netstat -an | findstr /C:":7443 " | findstr LISTENING >nul
if not errorlevel 1 (
    echo The game server is already running on port 7443.
    pause
    exit /b 0
)
if not exist "%~dp0logs" mkdir "%~dp0logs"
echo Game server log is also saved to %~dp0logs\server.log
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0tools\run_server.ps1' 2>&1 | ForEach-Object { $s = $_.ToString(); Write-Host $s; Add-Content -LiteralPath '%~dp0logs\server.log' -Value $s -Encoding UTF8 }"
echo.
echo The game server stopped.
pause
