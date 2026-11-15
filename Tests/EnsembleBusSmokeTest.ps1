$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project '.\EnsembleBusSmokeTest.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw "Ensemble smoke test failed. Exit code: $LASTEXITCODE" }
