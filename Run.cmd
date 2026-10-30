@echo off
cd /d "%~dp0"
echo KIEU VIET - Khoi dong ung dung quan ly tai san
echo Giu cua so nay mo trong khi su dung. Dung ung dung bang Ctrl+C.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start.ps1" %*
if errorlevel 1 pause
