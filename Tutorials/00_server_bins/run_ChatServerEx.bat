@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0ChatServerEx\%configuration%\net10.0" || exit /b 1
dotnet ChatServerEx.dll --uniqueID 1 --name ChatServerEx --port 32452 --maxConnectionNumber 256  --maxRequestLength 1024 --receiveBufferSize 16384 --sendBufferSize 16384 --roomMaxCountPerThread 12 --roomThreadCount 4 --roomMaxUserCount 4 --roomStartNumber 0 --dbWorkerThreadCount 4 --redisAddres 192.168.0.10
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
