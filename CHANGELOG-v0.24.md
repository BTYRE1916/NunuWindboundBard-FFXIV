# v0.24 — Concert Director: Stage Health & One-Click Preparation

Based on Windbound v0.23 Persistent Octet Lineups. In the Ensemble tab a new Concert Director pane is shown for Native Game Ensemble players.

The conductor can press PREPARE ENTIRE ORCHESTRA to reuse existing leader commands in order: sync MIDI file -> SET PARTY TRACKS -> SET PARTY INSTRUMENTS -> request stage status snapshots. Each client replies through authenticated FFXIV party chat with a compact local readiness snapshot: MIDI fingerprint, parts enabled, output ARMed, expanded 37-key keyboard, instrument ID and pending operations. The conductor sees each party member's status and the READY count. No new musical cue is generated: FFXIV native START remains the **only** trigger.

Automatic stage probes are limited to about one every 24 seconds during an explicitly active monitoring session; no constant chat polling. Source-only tests in `Tests/ConcertStageHealthSmokeTest.csproj` validate codec and preflight rules. The stage retry control in v0.24 re-runs complete preparation; targeted single-member retry is planned for v0.25.

Known limitation: detailed live statuses depend on receiving party-chat replies and current FFXIV UI/party data. Source inspection and ZIP validation were possible here; a Windows .NET 10 / Dalamud build and real multi-account test are still required.
