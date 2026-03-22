@echo off
set GUID=B2C3D4E5-F6A7-8901-2345-678901BCDEF0
set WIDGETS=%APPDATA%\G.SKILL\WigiDashManager\Widgets
set TARGET=%WIDGETS%\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "bin\Release\%GUID%.dll" "%TARGET%\" 2>nul
if errorlevel 1 copy /Y "bin\Debug\%GUID%.dll" "%TARGET%\"
copy /Y "bin\Release\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "bin\Debug\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "icon.png" "%TARGET%\" 2>nul

echo Deployed to %TARGET%
echo Restart WigiDash Manager to load the widget.
