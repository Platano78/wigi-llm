@echo off
echo Building LLM Control Center Widget...
set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not exist %MSBUILD% (
    set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\Tools\VsDevCmd.bat"
)
call "C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\Tools\VsDevCmd.bat" 2>nul
msbuild LLMControlCenterWidget.csproj /t:Build /p:Configuration=Debug /v:minimal
echo.
echo Build completed with exit code: %ERRORLEVEL%
