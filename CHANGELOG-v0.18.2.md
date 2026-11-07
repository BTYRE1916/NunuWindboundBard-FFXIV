# Windbound Bard v0.18.2 — Party Instrument Receiver Diagnostics Hotfix

## Why this update

Some conductors could select SET PARTY INSTRUMENTS and see their OWN Bard equip an instrument, while followers such as Edea did nothing. The previous code matched incoming party-chat sender text to the party leader's plain character name using an exact string match. That may reject valid cross-world sender names (e.g. `Name@World`), and it did not provide enough diagnostics to distinguish transport from follower configuration or instrument-switch failures.

## Changes

- Uses FFXIV sender player-link name/world identity (where available), or carefully validated sender text including world suffixes; resolves it uniquely against the actual active party roster.
- Still rejects non-party senders and commands from non-leaders. Duplicate same-name characters are not trusted unless world information disambiguates them.
- Handles verified party messages even when another plugin hides them from default chat output; commands remain restricted to Party/CrossParty chat.
- Live receiver diagnostics count incoming Windbound messages, accepted/rejected commands, last sender/channel and last rejection reason; no silent failure.
- In `Ensemble → CONDUCTOR FILE + INSTRUMENT SYNC`, expand `Party command receiver diagnostics` on Edea's client, and click **Copy party-command report**. This reveals why a follower did not switch.
- A submitted `/p` command is reported as **submitted**, not *delivered*: only a follower READY reply proves the command reached the recipient.
- Solo MIDI playback, native FFXIV START callback, auto Ready Check, instrument switch state machine, setlist and C3–C6 optimiser are unchanged.

## How to test

1. Both clients install and enable v0.18.2. Each enters the same actual FFXIV party. The conductor must be the actual FFXIV party leader.
2. On Edea's client, choose `Follower` (not Solo), turn on `Cross-PC/Game Ensemble` and `Automatically follow party leader's MIDI and instrument choices`. She does **not** need to accept each SET command manually.
3. The conductor selects the same MIDI, chooses an instrument for Edea, then clicks `SET PARTY INSTRUMENTS — auto-switch everyone`.
4. Look in Edea's party chat for `!windbound inst ...`; then in the receiver diagnostics see `messages seen`, `accepted` and `rejected`. If zero messages were seen, check the transport/configuration and both plugin versions; if it was rejected, the report explains why; if accepted but no switch, inspect `Instrument:` and `Switcher:` in the report.
5. Let the conductor see `Instruments: READY (1/1)` before starting FFXIV native Ensemble Ready Check. Never mistake the local instrument switching or a sent party command for remote confirmation.

## Build and limitations

Build the full Visual Studio solution with .NET 10 and Dalamud SDK 15. Run `Tests/Run-v0.18.2-Tests.ps1` on Windows. Package was source-reviewed, not compiled or game-tested in this environment. In-game validation with Edea is still required.
