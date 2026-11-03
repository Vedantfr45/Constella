@echo off
rem ---------------------------------------------------------------
rem  Constella: one-click build and run.
rem  Double-click this file. The first run installs the .NET 8 SDK
rem  (via winget) if it's missing, then builds and starts the app.
rem ---------------------------------------------------------------
setlocal
cd /d "%~dp0"
title Constella - build and run

where dotnet >nul 2>nul || goto :install
dotnet --list-sdks | findstr /r "^[89]\. ^1[0-9]\." >nul || goto :install
goto :build

:install
echo The .NET 8 SDK is not installed. Installing it now with winget (one time only)...
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
if errorlevel 1 (
    echo.
    echo Could not install it automatically.
    echo Download ".NET 8 SDK" from https://dotnet.microsoft.com/download/dotnet/8.0 , install it, then run this file again.
    pause
    exit /b 1
)
set "PATH=%ProgramFiles%\dotnet;%PATH%"

:build
echo Building Constella (takes about 20 seconds)...
rem Close a running copy so its files can be replaced.
taskkill /im Constella.exe /f >nul 2>nul
dotnet publish src\Constella.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o app --nologo -v quiet
if errorlevel 1 (
    echo.
    echo Build failed. Scroll up to see the error.
    pause
    exit /b 1
)

echo Starting Constella. Look for the star icon in the system tray.
start "" "%~dp0app\Constella.exe"
endlocal
