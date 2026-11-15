$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project .\InstrumentCatalogSmokeTest.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Instrument catalog test failed' }
