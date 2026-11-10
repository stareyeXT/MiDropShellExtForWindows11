@echo off
chcp 936 >nul
setlocal
cd /d "%~dp0"
rem Launch the installer with execution-policy bypass, so a zip downloaded from
rem the internet still runs. This is the only reason this .cmd exists.
rem Debug only, skip elevation:  install.cmd -NoElevate
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0°²×°.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%~1"=="" goto end
pause
:end
exit /b %RC%
