@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0GameServer_01\%configuration%\net10.0" || exit /b 1
dotnet GameServer_01.dll
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
