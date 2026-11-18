@echo off
setlocal
cd /d "%~dp0"
where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo Install the .NET 10 ASP.NET Core Runtime on this relay host.
    pause
    exit /b 1
)
dotnet "%~dp0WindboundOrchestraRelay.dll" --urls http://127.0.0.1:48523
if errorlevel 1 pause
