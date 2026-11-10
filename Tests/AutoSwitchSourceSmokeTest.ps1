# Structural checks; does not replace a Windows build or game test.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$s = Get-Content (Join-Path $root 'src\NunuWindboundBard\Core\InstrumentSwitcher.cs') -Raw
foreach ($token in @('Phase.RequestExit', 'Phase.WaitForExit', 'Phase.WaitAfterExit', 'performAction!(nativePerformanceInfo, 0u, 0)', 'IsActuallyUnequipped', 'TryClosePerformanceAddon', 'MarkExitConfirmed', 'SendNativeInstrumentAction(selected)', 'PerformanceModeWide', 'PerformanceMode')) {
  if (!$s.Contains($token)) { throw "Auto-switch source check failed: $token" }
}
Write-Host 'PASS: automatic Performance exit uses native instrument=0 stow and waits for FFXIV confirmation.'
Write-Host 'Note: build the solution and test in FFXIV to confirm native UI support.'
