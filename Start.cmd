@echo off
setlocal
start "" /wait "%~dp0HearthGloveCursor.exe" --start %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" echo Failed with code %result%. See the message above.
pause
exit /b %result%
