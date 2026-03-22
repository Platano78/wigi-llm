@echo off
set GUID=7ED895E1-9504-4B9E-A080-E2EB68275A0F
set TARGET=%APPDATA%\G.SKILL\WigiDashManager\Widgets\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "%~dp0bin\Debug\*" "%TARGET%\"
echo Deployed to %TARGET%
pause
