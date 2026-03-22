@echo off
echo Building LLMModelSelectorWidget...

REM Find MSBuild
set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not exist %MSBUILD% (
    set MSBUILD="C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
if not exist %MSBUILD% (
    echo MSBuild not found. Please install Visual Studio or Build Tools.
    pause
    exit /b 1
)

REM Build the project
%MSBUILD% LLMModelSelectorWidget.csproj /p:Configuration=Release /p:Platform="Any CPU" /v:minimal

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo Output: bin\Release\LLMModelSelectorWidget.dll
    echo.
    echo To install:
    echo 1. Copy DLL to WigiDash widgets folder
    echo 2. Restart WigiDash
    echo 3. Add widget from menu
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)

pause
