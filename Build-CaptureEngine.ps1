param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\CaptureEngine'),
    [string]$FfmpegPath = (Join-Path $PSScriptRoot 'ReadyToUse\Plugin\CaptureEngine\ffmpeg.exe')
)
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'src\WindboundCaptureEngine\WindboundCaptureEngine.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Native capture engine publish failed.' }
if (!(Test-Path -LiteralPath $FfmpegPath)) { throw 'Pass -FfmpegPath with a Windows ffmpeg.exe codec worker, or use the bundled ReadyToUse copy.' }
$destination = Join-Path $OutputDirectory 'ffmpeg.exe'
if ([IO.Path]::GetFullPath($FfmpegPath) -ne [IO.Path]::GetFullPath($destination)) { Copy-Item -LiteralPath $FfmpegPath -Destination $destination -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty\FFmpeg\LICENSE') -Destination (Join-Path $OutputDirectory 'FFmpeg-LICENSE.txt') -Force
Write-Host "Native engine: $OutputDirectory\WindboundCaptureEngine.exe" -ForegroundColor Green
Write-Host 'Open SHOW > Concert Capture > Native Windbound capture suite. No OBS runtime is required.'
