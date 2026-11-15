$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project .\GuitarToneLogic.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Guitar tone / arrangement logic test failed.' }
