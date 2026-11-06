$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$project = Join-Path $PSScriptRoot 'src\NunuWindboundBard\NunuWindboundBard.csproj'
Write-Host 'Building Nunu Windbound Bard for Dalamud API 15 (.NET 10)...' -ForegroundColor Magenta
dotnet build $project -c Debug -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See Visual Studio/.NET output.' }
Write-Host 'Use /xlsettings > Experimental > Dev Plugin Locations and select the built DLL.' -ForegroundColor Green
# The audio preview adds managed NuGet dependencies. Dalamud must see the
# dependent DLL files in the same output folder as NunuWindboundBard.dll.
$pluginDll = Get-ChildItem (Join-Path $PSScriptRoot 'src\NunuWindboundBard\bin') -Filter 'NunuWindboundBard.dll' -File -Recurse |
    Where-Object { $_.FullName -match '[\\/]Debug[\\]' } | Select-Object -First 1
if ($pluginDll) {
    foreach ($dependency in @('MeltySynth.dll', 'NAudio.dll', 'NAudio.Core.dll', 'NAudio.WinMM.dll')) {
        $candidate = Join-Path $pluginDll.DirectoryName $dependency
        if (Test-Path $candidate) {
            Write-Host "Audio dependency ready: $dependency" -ForegroundColor Green
        } else {
            Write-Warning "Audio dependency not found next to the plugin DLL: $dependency; check NuGet restore and output folder."
        }
    }
}

Get-ChildItem (Join-Path $PSScriptRoot 'src\NunuWindboundBard\bin') -Filter 'NunuWindboundBard.dll' -File -Recurse | Select-Object -ExpandProperty FullName
