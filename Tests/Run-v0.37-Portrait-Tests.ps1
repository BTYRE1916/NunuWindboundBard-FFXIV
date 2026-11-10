# Run on Windows with .NET SDK 10 installed.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'MemberPortraitFilesSmokeTest.csproj'
Write-Host "Running v0.37 portrait-cache smoke tests..."
dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Member portrait smoke test failed.' }
Write-Host "Then run the plugin project Build / Rebuild in Visual Studio (Dalamud SDK 15)."
