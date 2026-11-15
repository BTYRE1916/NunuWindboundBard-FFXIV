$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.27 - MIDI Audio Preview smoke tests' -ForegroundColor Magenta

dotnet run --project .\AudioPreviewMidiBuilderSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Audio preview SMF note-generation regression test FAILED.' }

$src = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$main = Get-Content (Join-Path $src 'MainWindow.cs') -Raw
$plugin = Get-Content (Join-Path $src 'Plugin.cs') -Raw
$ui = Get-Content (Join-Path $src 'MainWindow.AudioPreview.cs') -Raw
$config = Get-Content (Join-Path $src 'Configuration.cs') -Raw
$csproj = Get-Content (Join-Path $src 'NunuWindboundBard.csproj') -Raw
if (-not $main.Contains('DrawAudioPreview()')) { throw 'Audio Preview tab missing.' }
if (-not $plugin.Contains('AudioPreview.Poll();')) { throw 'Audio preview framework lifecycle missing.' }
if (-not $ui.Contains('AudioPreviewMidiBuilder.Build(')) { throw 'Arrangement/track preview missing.' }
if (-not $config.Contains('PreviewSoundFontPath')) { throw 'SoundFont persistence missing.' }
if (-not $csproj.Contains('MeltySynth') -or -not $csproj.Contains('NAudio')) { throw 'Audio packages missing.' }
if (-not $csproj.Contains('<Version>0.27.0.0</Version>')) { throw 'Version mismatch.' }
Write-Host 'Source integration checks passed.' -ForegroundColor Green
Write-Host 'Build the full Dalamud project to validate the audio device/NuGet output.' -ForegroundColor Yellow
