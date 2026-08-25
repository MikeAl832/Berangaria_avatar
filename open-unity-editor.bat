@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if /I "%~1"=="--help" goto :help

set "UNITY_EDITOR=C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
if not exist "%UNITY_EDITOR%" (
    echo Unity 2022.3.62f3 not found:
    echo %UNITY_EDITOR%
    pause
    exit /b 1
)

start "Berangaria Avatar Unity" "%UNITY_EDITOR%" -projectPath "%CD%"
exit /b 0

:help
echo Usage: open-unity-editor.bat
echo Opens this project in the verified Unity 2022.3.62f3 editor.
exit /b 0
