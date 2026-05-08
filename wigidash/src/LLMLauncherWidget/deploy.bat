@echo off
set GUID=B8C9D0E1-F2A3-4567-8901-BCDEF1234567
set WIDGETS=%APPDATA%\G.SKILL\WigiDashManager\Widgets
set TARGET=%WIDGETS%\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "bin\Release\%GUID%.dll" "%TARGET%\" 2>nul
if errorlevel 1 copy /Y "bin\Debug\%GUID%.dll" "%TARGET%\"
copy /Y "bin\Release\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "bin\Debug\%GUID%.pdb" "%TARGET%\" 2>nul
copy /Y "icon.png" "%TARGET%\" 2>nul

REM Copy your buttons.json + model icons separately. They are not bundled with
REM the DLL because they're per-user config. See wigidash/examples/buttons-starter.json
REM and wigidash/icons/ in the wigi-llm repo.

echo Deployed to %TARGET%
echo Drop your buttons.json and icons into that folder, then restart WigiDash Manager.
