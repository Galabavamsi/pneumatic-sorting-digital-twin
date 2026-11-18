@echo off
rem Second development server (port 8082), for when 8081 is already taken.
set "PATH=C:\Program Files\nodejs;%PATH%"
cd /d "%~dp0"
npx expo start --web --port 8082
