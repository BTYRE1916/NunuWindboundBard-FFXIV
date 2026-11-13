param([string]$FfmpegPath = '', [switch]$NativeWindows)
$ErrorActionPreference = 'Stop'
foreach ($project in @('CaptureStudioTests', 'ObsCaptureProtocolSmokeTest', 'ObsRecorderTransportSmokeTest')) {
    dotnet run --project (Join-Path $PSScriptRoot "$project.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "$project failed." }
}
if ($FfmpegPath) {
    dotnet run --project (Join-Path $PSScriptRoot 'NativeOutputPipelineSmoke.csproj') -c Release -- $FfmpegPath
    if ($LASTEXITCODE -ne 0) { throw 'Native output pipeline smoke test failed.' }
}
if ($NativeWindows) {
    if (!$FfmpegPath) { throw '-NativeWindows also requires -FfmpegPath.' }
    dotnet run --project (Join-Path $PSScriptRoot 'CaptureStudioWindowsSmoke.csproj') -c Release -- $FfmpegPath
    if ($LASTEXITCODE -ne 0) { throw 'Windows native composition smoke test failed.' }
}
