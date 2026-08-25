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

start "Berangaria Avatar" "%AVATAR_EXE%"
exit /b 0

:help
echo Usage: start-avatar.bat
echo Starts the local Berangaria Avatar Windows build.
exit /b 0
