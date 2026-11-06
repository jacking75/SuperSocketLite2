@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0MultiPortServer\%configuration%\net10.0" || exit /b 1
dotnet MultiPortServer.dll  --name1 clientSide --port1 32452 --maxConnectionNumber1 256  --name2 serverSide --port2 11021 --maxConnectionNumber2 16
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
