@echo off
chcp 936 >nul
setlocal
cd /d "%~dp0"
rem Launch the uninstaller with execution-policy bypass, so a zip downloaded
rem from the internet still runs.
rem Debug only, skip elevation:  uninstall.cmd -NoElevate
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0п╤ть.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%~1"=="" goto end
pause
:end
exit /b %RC%
