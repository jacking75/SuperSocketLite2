@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0EchoServerEx\%configuration%\net10.0" || exit /b 1
dotnet EchoServerEx.dll --port 32452 --maxConnectionNumber 32 --name EchoServer
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
