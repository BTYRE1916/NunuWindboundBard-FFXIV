# Structural checks; does not replace a Windows build or game test.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$s = Get-Content (Join-Path $root 'src\NunuWindboundBard\Core\InstrumentSwitcher.cs') -Raw
foreach ($token in @('Phase.RequestExit', 'Phase.WaitForExit', 'Phase.WaitAfterExit', 'TryClosePerformanceAddon', 'Close(true)', 'MarkExitConfirmed', 'SendNativeInstrumentAction(selected)', 'PerformanceModeWide', 'PerformanceMode')) {
  if (!$s.Contains($token)) { throw "Auto-switch source check failed: $token" }
}
Write-Host 'PASS: automatic Performance exit source state machine is present.'
Write-Host 'Note: build the solution and test in FFXIV to confirm native UI support.'
