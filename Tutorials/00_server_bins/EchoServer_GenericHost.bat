@echo off
setlocal
set "configuration=%~1"
if not defined configuration set "configuration=Release"
pushd "%~dp0EchoServer_GenericHost\%configuration%\net10.0" || exit /b 1
dotnet EchoServer_GenericHost.dll
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%
