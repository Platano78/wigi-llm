@echo off
echo Building Context Monitor Widget...
call "C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\Tools\VsDevCmd.bat" 2>nul
msbuild ContextMonitorWidget.csproj /t:Build /p:Configuration=Release /v:minimal
echo.
echo Build completed with exit code: %ERRORLEVEL%
