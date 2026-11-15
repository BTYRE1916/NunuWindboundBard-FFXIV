$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.22.2 — track meta and octave regression tests' -ForegroundColor Magenta
& (Join-Path $PSScriptRoot 'Run-v0.22-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Metadata regression tests failed.' }

$root = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$reader = Get-Content (Join-Path $root 'Core\MidiFileReader.cs') -Raw
$ui = Get-Content (Join-Path $root 'MainWindow.cs') -Raw
$metadata = Get-Content (Join-Path $root 'MainWindow.MidiMetadata.cs') -Raw
$tracks = Get-Content (Join-Path $root 'Core\MidiTrackTags.cs') -Raw
if (-not $reader.Contains('meta is 0x01 or 0x03 or 0x04 or 0x06 or 0x07')) { throw 'FF01/06/07 reader missing.' }
if (-not $metadata.Contains('BuildMidiMetadataReport')) { throw 'Missing metadata diagnostics.' }
if (-not $tracks.Contains('instrument?.TagMetadata')) { throw 'Missing metadata tag lookup.' }
if (-not $ui.Contains('GetMidiSoloInstrumentSuggestion()')) { throw 'Solo is not linked to MIDI inference.' }
Write-Host 'v0.22.2 source and core MIDI metadata tests passed.' -ForegroundColor Green
