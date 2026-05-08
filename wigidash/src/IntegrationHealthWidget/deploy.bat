@echo off
set GUID=8977786C-4B1C-4605-9F2C-E2DA44567771
set WIDGETS=C:\Users\Aldwin\AppData\Roaming\G.SKILL\WigiDashManager\Widgets
set TARGET=%WIDGETS%\%GUID%

if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "bin\Debug\%GUID%.dll" "%TARGET%\"
copy /Y "bin\Debug\%GUID%.pdb" "%TARGET%\" 2>nul
echo Deployed to %TARGET%
echo Restart WigiDash Manager to load the widget.
