@echo off
chcp 65001 >nul
cd /d "%~dp0"
where node >nul 2>nul
if errorlevel 1 (
  echo 请先安装 Node.js 22 或更新版本。
  pause
  exit /b 1
)
node launch.cjs
if errorlevel 1 (
  pause
  exit /b 1
)
start "" "http://127.0.0.1:18765"
if exist "private\首次登录.txt" start "" notepad.exe "private\首次登录.txt"
pause
