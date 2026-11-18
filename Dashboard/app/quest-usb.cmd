@echo off
rem Shows the dashboard's AR view on a Meta Quest plugged in by USB.
rem Needs the development server running (start-web.cmd) and USB debugging
rem allowed in the headset. Wear the headset before running this: its browser
rem only opens the page while the headset is awake.
rem   quest-usb.cmd            AR view with all stations, server on port 8081
rem   quest-usb.cmd kit1       one station at full size
rem   quest-usb.cmd all 8082   another server port
setlocal
set "WHAT=%~1"
if "%WHAT%"=="" set "WHAT=all"
set "PORT=%~2"
if "%PORT%"=="" set "PORT=8081"

rem adb comes with Unity's Android Build Support; any other copy on PATH works too
set "ADB=adb"
where adb >nul 2>nul || set "ADB=C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"

"%ADB%" devices
rem the headset's "localhost" now reaches this PC; WebXR needs localhost or https
"%ADB%" reverse tcp:%PORT% tcp:%PORT% || goto :failed
rem opened through the Quest shell: a plain VIEW intent starts the browser without showing the page
"%ADB%" shell am start -n com.oculus.vrshell/.MainActivity -d apk://com.oculus.browser -e uri "http://localhost:%PORT%/?ar=%WHAT%"
echo.
echo In the headset: the browser shows the AR page. Press "Enter AR".
goto :eof

:failed
echo.
echo The headset is not reachable. Plug it in, put it on and accept "Allow USB debugging".
