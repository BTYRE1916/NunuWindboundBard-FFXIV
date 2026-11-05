$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$project = Join-Path $PSScriptRoot 'src\NunuWindboundBard\NunuWindboundBard.csproj'
Write-Host 'Building Nunu Windbound Bard for Dalamud API 15 (.NET 10)...' -ForegroundColor Magenta
dotnet build $project -c Debug -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See Visual Studio/.NET output.' }
Write-Host 'Use /xlsettings > Experimental > Dev Plugin Locations and select the built DLL.' -ForegroundColor Green
Get-ChildItem (Join-Path $PSScriptRoot 'src\NunuWindboundBard\bin') -Filter 'NunuWindboundBard.dll' -File -Recurse | Select-Object -ExpandProperty FullName
