@echo off
setlocal

set SCRIPT_DIR=%~dp0
set REPO_ROOT=%SCRIPT_DIR%..

set AVALONIA_TELEMETRY_OPTOUT=1

dotnet run --project "%SCRIPT_DIR%_build.csproj" -- --root "%REPO_ROOT%" %*
