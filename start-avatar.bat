@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if /I "%~1"=="--help" goto :help

set "AVATAR_EXE=%CD%\Builds\Windows\BerangariaAvatar.exe"
if not exist "%AVATAR_EXE%" (
    echo Avatar build not found.
    echo Run: Berangaria ^> Build Windows Avatar
    pause
    exit /b 1
)

start "Berangaria Avatar" "%AVATAR_EXE%" -screen-fullscreen 0 -force-d3d11 -force-d3d11-bitblt-model
exit /b 0

:help
echo Usage: start-avatar.bat
echo Starts the local Berangaria Avatar Windows build.
echo Ctrl+Shift+F8 toggles placement mode; drag to move and use the wheel to scale.
exit /b 0
