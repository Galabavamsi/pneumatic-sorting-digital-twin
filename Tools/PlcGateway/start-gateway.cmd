@echo off
rem Starts the read-only PLC gateway that feeds the dashboard app.
rem Double-click this file and leave the window open. Close the window to stop it.
title PLC gateway (leave this window open)
cd /d "%~dp0"
dotnet run
echo.
echo The gateway has stopped. Press any key to close this window.
pause >nul
