@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0GameServer_MoDedicated2\%configuration%\net10.0" || exit /b 1
dotnet GameServer_MoDedicated2.dll --uniqueID 1 --name GameServer2 --port 32452 --maxConnectionNumber 256  --maxRequestLength 1024 --receiveBufferSize 16384 --sendBufferSize 16384 --roomMaxCount 1000 --roomMaxUserCount 4 --roomStartNumber 0
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
