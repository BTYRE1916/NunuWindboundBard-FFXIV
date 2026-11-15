$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projects = @('PlaylistProfilesSmokeTest.csproj','PartyAssignmentSmokeTest.csproj')
foreach ($project in $projects) {
    Write-Host "Testing $project" -ForegroundColor Cyan
    dotnet run --project (Join-Path $root $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Test failed: $project" }
}
Write-Host 'Both new feature smoke tests passed.' -ForegroundColor Green
