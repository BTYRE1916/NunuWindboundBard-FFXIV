# Check a release ZIP *before* publishing it to GitHub.
# Example: powershell -ExecutionPolicy Bypass -File .\Test-WindboundRelease.ps1 .\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip
[CmdletBinding()]
param([Parameter(Mandatory=$true,Position=0)][string]$ZipPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw "ZIP not found: $ZipPath" }
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
try {
    $dll = $zip.GetEntry('NunuWindboundBard.dll')
    $json = $zip.GetEntry('NunuWindboundBard.json')
    if (-not $dll) { throw 'Invalid ZIP: NunuWindboundBard.dll not at ZIP root. Did you zip the source folder?' }
    if (-not $json) { throw 'Invalid ZIP: NunuWindboundBard.json not at ZIP root. Plugin.json is the repository listing, not the local plugin manifest.' }
    if ($dll.Length -le 0) { throw 'Invalid ZIP: plugin DLL is empty.' }
    $reader = New-Object System.IO.StreamReader($json.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if ($manifest -is [array]) { throw 'Invalid local manifest: expected a JSON object, not an array.' }
    if ($manifest.InternalName -ne 'NunuWindboundBard') { throw "Invalid InternalName: $($manifest.InternalName)" }
    if ($manifest.AssemblyVersion -ne '0.46.4.0') { throw "Invalid AssemblyVersion: $($manifest.AssemblyVersion)" }
    if ([int]$manifest.DalamudApiLevel -ne 15) { throw "Invalid DalamudApiLevel: $($manifest.DalamudApiLevel)" }
    Write-Host "PASS: valid v0.46.4 / API 15 manifest and plugin DLL at archive root: $ZipPath" -ForegroundColor Green
} finally { $zip.Dispose() }
