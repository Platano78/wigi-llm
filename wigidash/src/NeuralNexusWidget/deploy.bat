@echo off
set GUID=48B421E2-91C8-4B92-9CF8-F8E6C9BDACBE
set TARGET=%APPDATA%\G.SKILL\WigiDashManager\Widgets\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "%~dp0bin\Release\%GUID%.dll" "%TARGET%\"
copy /Y "%~dp0bin\Release\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "icon.png" "%TARGET%\" 2>nul
echo Deployed to %TARGET%
