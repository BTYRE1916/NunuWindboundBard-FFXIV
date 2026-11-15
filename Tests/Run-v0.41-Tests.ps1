param([string]$FfmpegPath = '')
$ErrorActionPreference = 'Stop'
foreach ($project in @('CaptureStudioTests', 'ObsCaptureProtocolSmokeTest', 'ObsRecorderTransportSmokeTest', 'CaptureStudioWindowsSmoke')) {
    if ($project -eq 'CaptureStudioWindowsSmoke' -and !$FfmpegPath) { continue }
    if ($project -eq 'CaptureStudioWindowsSmoke') {
        dotnet run --project (Join-Path $PSScriptRoot "$project.csproj") -c Release -- $FfmpegPath
    } else {
        dotnet run --project (Join-Path $PSScriptRoot "$project.csproj") -c Release
    }
    if ($LASTEXITCODE -ne 0) { throw "$project failed." }
}
