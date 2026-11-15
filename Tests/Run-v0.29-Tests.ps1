$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.29 — Conductor Party ARM regression suite' -ForegroundColor Magenta
& (Join-Path $PSScriptRoot 'Run-v0.28-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing Concert Snapshots tests failed.' }
dotnet run --project .\NativePartyArmSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Party ARM / DISARM protocol tests failed.' }
$dir = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$plugin = Get-Content (Join-Path $dir 'Plugin.cs') -Raw
$main = Get-Content (Join-Path $dir 'MainWindow.PartyArming.cs') -Raw
$game = Get-Content (Join-Path $dir 'MainWindow.GameEnsemble.cs') -Raw
$chat = Get-Content (Join-Path $dir 'Core\GamePartyChatSync.cs') -Raw
$config = Get-Content (Join-Path $dir 'Configuration.cs') -Raw
if (-not $plugin.Contains('main.PollNativePartyArming()')) { throw 'Framework update does not poll party ARM.' }
if (-not $main.Contains('HudSetOutputArmed(true)')) { throw 'Follower ARM not wired to existing output setting.' }
if (-not $main.Contains('NativePartyArmPlan.Evaluate')) { throw 'Safety preflight missing.' }
if (-not $game.Contains('ReceiveNativePartyArm(cue)')) { throw 'Follower party receive dispatch missing.' }
if (-not $chat.Contains('GamePartyCueKind.NativeArmReady or GamePartyCueKind.NativeArmFailed')) { throw 'Leader ACK receive wiring missing.' }
if (-not $config.Contains('public bool AllowLeaderArmCommands')) { throw 'Follower opt-out missing.' }
Write-Host 'PASS: party ARM protocol and source integration. Real FFXIV live test still necessary.' -ForegroundColor Green
