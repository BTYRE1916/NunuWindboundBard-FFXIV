[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$solution = Join-Path $projectRoot 'NunuWindboundBard.sln'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw 'NunuWindboundBard.sln must be beside Build-Plugin.ps1.'
}

Write-Host "Building plugin and native capture engine ($Configuration | x64)..." -ForegroundColor Magenta
# The separate engine has no plugin assembly reference, so build the solution.
$buildArguments = @(
    'build', $solution, '-c', $Configuration,
    '-p:Platform=x64', '-p:EnableWindowsTargeting=true',
    '--no-incremental', '-m:1', '-nr:false', '-p:UseSharedCompilation=false'
)
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed. See the .NET output above.' }

$pluginOutput = Join-Path $projectRoot "src\NunuWindboundBard\bin\x64\$Configuration"
$pluginDll = Join-Path $pluginOutput 'NunuWindboundBard.dll'
$engineOutput = Join-Path $projectRoot "src\WindboundCaptureEngine\bin\x64\$Configuration\net10.0-windows10.0.19041.0"
$engineDll = Join-Path $engineOutput 'WindboundCaptureEngine.dll'
foreach ($builtFile in @($pluginDll, $engineDll)) {
    if (-not (Test-Path -LiteralPath $builtFile -PathType Leaf)) {
        throw "Expected build output is missing: $builtFile"
    }
}

Write-Host "Plugin built: $pluginDll" -ForegroundColor Green
Write-Host "Capture engine built: $engineDll" -ForegroundColor Green
foreach ($dependency in @('MeltySynth.dll', 'NAudio.dll', 'NAudio.Core.dll', 'NAudio.WinMM.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginOutput $dependency))) {
        Write-Warning "Audio dependency not found beside the plugin: $dependency"
    }
}
Write-Host 'To publish a standalone Windows capture EXE, run Build-CaptureEngine.ps1.'
Write-Host 'For the supplied complete binaries, use ReadyToUse/Plugin/NunuWindboundBard.dll and keep its CaptureEngine folder.'
