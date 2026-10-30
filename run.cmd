@echo off
rem Launches Voltide via in-memory compilation.
rem Used because Smart App Control blocks unsigned .exe files on this machine.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0run.ps1"
