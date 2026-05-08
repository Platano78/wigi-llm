@echo off
echo Building Integration Health Widget...
call "C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\Tools\VsDevCmd.bat" 2>nul
msbuild IntegrationHealthWidget.csproj /t:Build /p:Configuration=Release /v:minimal
echo.
echo Build completed with exit code: %ERRORLEVEL%
