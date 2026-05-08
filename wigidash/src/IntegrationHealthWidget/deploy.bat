@echo off
set GUID=8977786C-4B1C-4605-9F2C-E2DA44567771
set TARGET=%APPDATA%\G.SKILL\WigiDashManager\Widgets\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "%~dp0bin\Release\%GUID%.dll" "%TARGET%\"
copy /Y "%~dp0bin\Release\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "%~dp0icon.png" "%TARGET%\" 2>nul
echo Deployed to %TARGET%
echo Restart WigiDash Manager to load the widget.
