@echo off
setlocal
title RadioV2 Build

set ISCC="%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
set SCRIPT="%~dp0RadioV2Setup.iss"
set PROJECT="%~dp0..\RadioV2.csproj"
set PUBLISH_DIR="%~dp0..\publish"

echo ============================================
echo  Step 1: dotnet publish
echo ============================================
echo.

dotnet publish %PROJECT% -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
    -o %PUBLISH_DIR%

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: dotnet publish failed. Fix the build errors above before packaging.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  Step 2: Inno Setup compile
echo ============================================
echo.

if not exist %ISCC% (
    echo ERROR: Inno Setup not found at %ISCC%
    echo Install it from https://jrsoftware.org/isinfo.php
    pause
    exit /b 1
)

%ISCC% %SCRIPT%

if %ERRORLEVEL% == 0 (
    echo.
    echo ============================================
    echo  Done! Installer\Output\RadioV2Setup.exe
    echo ============================================
) else (
    echo.
    echo ERROR: Inno Setup failed with code %ERRORLEVEL%
)

pause
