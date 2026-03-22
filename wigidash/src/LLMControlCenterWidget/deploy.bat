@echo off
set GUID=57AA91B9-1E54-45CA-A05C-89326A8FBBDD
set WIDGETS=%APPDATA%\G.SKILL\WigiDashManager\Widgets
set TARGET=%WIDGETS%\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "bin\Debug\%GUID%.dll" "%TARGET%\"
copy /Y "bin\Debug\%GUID%.pdb" "%TARGET%\" 2>nul

REM Copy framework DLL
copy /Y "WigiDashWidgetFramework.dll" "%TARGET%\" 2>nul

echo Deployed to %TARGET%
echo Restart WigiDash Manager to load the widget.
