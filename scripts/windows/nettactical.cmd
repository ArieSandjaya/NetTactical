@echo off
rem Runs nettactical.ps1 without changing the PowerShell execution policy for the machine.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0nettactical.ps1" %*
