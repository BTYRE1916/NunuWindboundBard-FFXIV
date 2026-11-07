$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\NunuWindboundBard'
$player = Get-Content (Join-Path $root 'Core\AudioPreviewPlayer.cs') -Raw
$project = Get-Content (Join-Path $root 'NunuWindboundBard.csproj') -Raw
if ($player.Contains('sequencer.MidiFile')) { throw 'Unsupported MeltySynth sequencer.MidiFile access remains.' }
if (-not $player.Contains('new PreviewSamples(prepared.Sequencer, prepared.File.Length, volume)')) {
    throw 'Preview duration is not passed from loaded MidiFile.Length.'
}
if (-not $player.Contains('public TimeSpan Duration => duration;')) {
    throw 'Preview duration is not stored in the sample provider.'
}
if (-not $project.Contains('<Version>0.27.1.0</Version>')) { throw 'Version not updated.' }
Write-Host 'PASS: MeltySynth 2.4.1 MIDI duration API compatibility (source-level).' -ForegroundColor Green
Write-Host 'Run Build-Plugin.ps1 to confirm compiled plugin with Dalamud/.NET on Windows.' -ForegroundColor Yellow
