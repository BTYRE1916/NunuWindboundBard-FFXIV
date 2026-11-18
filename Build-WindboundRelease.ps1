# Builds and packs a *compiled* Dalamud install release. Never upload the full-project source ZIP as the install asset.
# Example: powershell -ExecutionPolicy Bypass -File .\Build-WindboundRelease.ps1
# Extra studio files: .\Build-WindboundRelease.ps1 -CaptureEngineDirectory 'C:\path\to\published\engine' -FfmpegExe 'C:\path\to\ffmpeg.exe'
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [string]$CaptureEngineDirectory = '',
    [string]$FfmpegExe = '',
    [string]$SdkZip = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectRoot = $PSScriptRoot
$pluginDir = Join-Path $projectRoot 'src\NunuWindboundBard'
$projectFile = Join-Path $pluginDir 'NunuWindboundBard.csproj'
$repositoryManifest = Join-Path $projectRoot 'Plugin.json'
$internalName = 'NunuWindboundBard'
$version = '0.46.4.0'
$releaseVersion = '0.46.4'
$outDir = Join-Path $projectRoot 'dist'
$releaseName = "FFXIVMIDIPLAYER-BY-NUNU-v$releaseVersion.zip"
$releasePath = Join-Path $outDir $releaseName

function Assert-ReleaseDirectory([string]$directory) {
    $dll = Join-Path $directory "$internalName.dll"
    $manifestPath = Join-Path $directory "$internalName.json"
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Release is missing $internalName.dll at ZIP root (actual staging directory: $directory)."
    }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Release is missing $internalName.json at ZIP root (actual staging directory: $directory). Do not use Plugin.json or the full-project source ZIP."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest -is [array]) { throw "Invalid $internalName.json: expected a single plugin manifest object, not the repository manifest array." }
    if ($manifest.InternalName -ne $internalName) { throw "Incorrect InternalName: '$($manifest.InternalName)' expected '$internalName'." }
    if ($manifest.AssemblyVersion -ne $version) { throw "Incorrect AssemblyVersion: '$($manifest.AssemblyVersion)' expected '$version'." }
    if ([int]$manifest.DalamudApiLevel -ne 15) { throw "Unexpected DalamudApiLevel: '$($manifest.DalamudApiLevel)'; expected 15." }
    if ((Get-Item -LiteralPath $dll).Length -le 0) { throw 'Plugin DLL is empty.' }
}

$remoteEntry = @(Get-Content -LiteralPath $repositoryManifest -Raw | ConvertFrom-Json)
if ($remoteEntry.Count -ne 1 -or $remoteEntry[0].InternalName -ne $internalName -or $remoteEntry[0].AssemblyVersion -ne $version) {
    throw 'The repository Plugin.json does not match the build version. Update it before publishing.'
}
if (-not $SkipBuild) {
    Write-Host 'Building the Dalamud plugin (Release/x64)...' -ForegroundColor Cyan
    & dotnet build $projectFile -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed. No release ZIP created.' }
}

$bin = Join-Path $pluginDir 'bin'
if (-not (Test-Path -LiteralPath $bin)) { throw "No build output found at $bin. Build the plugin first." }
$zipCandidate = $null
if ($SdkZip) {
    if (-not (Test-Path -LiteralPath $SdkZip -PathType Leaf)) { throw "SDK zip does not exist: $SdkZip" }
    $zipCandidate = (Resolve-Path -LiteralPath $SdkZip).Path
} else {
    # Dalamud.NET.Sdk / DalamudPackager normally creates a latest.zip after Release build.
    $candidates = @(Get-ChildItem -LiteralPath $bin -Recurse -Filter 'latest.zip' -File |
        Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
        Sort-Object LastWriteTime -Descending)
    if ($candidates.Count -gt 0) { $zipCandidate = $candidates[0].FullName }
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$staging = Join-Path $outDir '_windbound_release_staging'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    if ($zipCandidate) {
        Write-Host "Staging SDK zip: $zipCandidate" -ForegroundColor Cyan
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zipCandidate, $staging)
    } else {
        # Some SDK configurations emit loose files only; use the directory with the generated local manifest.
        $manifests = @(Get-ChildItem -LiteralPath $bin -Filter "$internalName.json" -Recurse -File |
            Where-Object { $_.FullName -match [regex]::Escape($Configuration) -and (Test-Path -LiteralPath (Join-Path $_.Directory.FullName "$internalName.dll")) } |
            Sort-Object LastWriteTime -Descending)
        if ($manifests.Count -eq 0) {
            throw "No SDK zip or build directory with $internalName.dll and $internalName.json found. Check that the SDK produced a Release package."
        }
        $looseDir = $manifests[0].Directory.FullName
        Write-Host "Staging SDK build directory: $looseDir" -ForegroundColor Cyan
        Get-ChildItem -LiteralPath $looseDir -Force | Where-Object { $_.Name -ne 'latest.zip' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $staging -Recurse -Force }
    }
    Assert-ReleaseDirectory $staging

    if ($CaptureEngineDirectory) {
        if (-not (Test-Path -LiteralPath $CaptureEngineDirectory -PathType Container)) { throw "CaptureEngineDirectory does not exist: $CaptureEngineDirectory" }
        $destEngine = Join-Path $staging 'CaptureEngine'
        New-Item -ItemType Directory -Path $destEngine -Force | Out-Null
        Get-ChildItem -LiteralPath $CaptureEngineDirectory -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $destEngine -Recurse -Force
        }
        if (-not (Test-Path -LiteralPath (Join-Path $destEngine 'WindboundCaptureEngine.exe') -PathType Leaf)) {
            throw 'The supplied CaptureEngineDirectory has no WindboundCaptureEngine.exe at its root.'
        }
    }
    if ($FfmpegExe) {
        if (-not (Test-Path -LiteralPath $FfmpegExe -PathType Leaf)) { throw "FFmpeg executable not found: $FfmpegExe" }
        $destEngine = Join-Path $staging 'CaptureEngine'
        New-Item -ItemType Directory -Path $destEngine -Force | Out-Null
        Copy-Item -LiteralPath $FfmpegExe -Destination (Join-Path $destEngine 'ffmpeg.exe') -Force
    }
    Assert-ReleaseDirectory $staging
    if (Test-Path -LiteralPath $releasePath) { Remove-Item -LiteralPath $releasePath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $releasePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # Verify the *finished* ZIP, not just its staging directory. A project wrapper folder is never acceptable.
    $checkZip = [System.IO.Compression.ZipFile]::OpenRead($releasePath)
    try {
        $actualEntries = @($checkZip.Entries | ForEach-Object { $_.FullName.Replace('\\', '/') })
        foreach ($name in @("$internalName.json", "$internalName.dll")) {
            if ($actualEntries -cnotcontains $name) { throw "Invalid release ZIP: '$name' is not at archive root." }
        }
    } finally { $checkZip.Dispose() }

    Write-Host "SUCCESS: $releasePath" -ForegroundColor Green
    Write-Host 'Upload THIS file as the GitHub release asset for tag 0.46.4 (not the source ZIP).'
    if (-not $CaptureEngineDirectory -or -not $FfmpegExe) {
        Write-Warning 'Studio CaptureEngine/ffmpeg were not both provided. If you use built-in concert recording, pass both paths to include those binaries.'
    }
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
