@echo off
rem ---------------------------------------------------------------
rem  Constella: build the download for a GitHub release.
rem  Makes one self-contained Constella.exe (no .NET needed on the
rem  other PC) and zips it to release\Constella-win-x64.zip.
rem ---------------------------------------------------------------
setlocal
cd /d "%~dp0"
title Constella - make release

echo Building Constella.exe (takes about a minute)...
dotnet publish src\Constella.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o release\Constella --nologo -v quiet
if errorlevel 1 (
    echo.
    echo Build failed. Scroll up to see the error.
    pause
    exit /b 1
)

echo Zipping...
powershell -NoProfile -Command "Compress-Archive -Path 'release\Constella\Constella.exe' -DestinationPath 'release\Constella-win-x64.zip' -Force"

echo.
echo Done: release\Constella-win-x64.zip
echo Upload that file to your GitHub release.
explorer release
pause
endlocal
