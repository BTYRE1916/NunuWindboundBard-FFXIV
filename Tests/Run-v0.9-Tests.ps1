$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
  & dotnet run --project .\MultiDeviceTransportSmokeTest.csproj -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Multi-Device network transport test failed.' }
  & .\Run-v0.8-Tests.ps1
  if ($LASTEXITCODE -ne 0) { throw 'Existing saved playlist and party test suite failed.' }
  Write-Host 'Windbound v0.9 transport and existing feature tests passed.' -ForegroundColor Green
} finally { Pop-Location }
