@echo off
rem Rebuilds MX Studio as one portable exe, closes the running window, copies the
rem new exe to D:\Programs\MX Studio.exe and starts it again. See deploy.ps1.
rem Usage: deploy.cmd [-Destination <path to MX Studio.exe>] [-SkipBuild]
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" %*
exit /b %errorlevel%
