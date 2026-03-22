@echo off
set PATH=C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin;%PATH%
cd /d "%~dp0"
msbuild LLMBrainMonitorWidget.csproj /t:Build /p:Configuration=Debug /v:minimal
if %ERRORLEVEL% NEQ 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)
echo BUILD SUCCEEDED
pause
