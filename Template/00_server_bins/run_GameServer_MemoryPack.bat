@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0GameServer_MemoryPack\%configuration%\net10.0" || exit /b 1
dotnet GameServer_MemoryPack.dll --Port 32452 --MaxUserCount 100 --Name "GameServer_MemoryPack"
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
