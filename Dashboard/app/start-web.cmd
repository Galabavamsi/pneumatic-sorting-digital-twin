@echo off
rem Starts the dashboard in a browser (development server).
set "PATH=C:\Program Files\nodejs;%PATH%"
cd /d "%~dp0"
npx expo start --web --port 8081
