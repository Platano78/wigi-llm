@echo off
set GUID=A1B2C3D4-E5F6-7890-1234-567890ABCDEF
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
