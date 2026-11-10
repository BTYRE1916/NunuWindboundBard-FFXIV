# Nunu's Windbound Bard v0.39.0 — Dark Stage UI

**UI-only visual redesign** built on v0.38.1. This is a source package for
Visual Studio / Dalamud API 15, **not** a verified DLL release.

## New

- Soul Weeper concert header with song, version and subtle optional neon lighting.
- Compact live-state chips for Solo / Conductor / Follower, FFXIV keyboard readiness,
  direct MIDI ARM, and playback activity. Tooltips and a collapsible detail log
  preserve diagnostic messages.
- Six workspaces replacing the overcrowded 16-item top navigation:
  PERFORMANCE, BAND, STUDIO, SHOW, TOOLS, SETUP. All original feature views remain.
- Three selectable accent presets: Soul Rose, Void Violet and Aether Cyan.
- Optional reduced-motion mode (disables the animated underline).
- Plain-colours fallback, available from the header and Setup > Appearance.
- Larger colour-coded transport, practice and Showmaster controls.
- Enhanced Piano Academy fall-note highlights, pressed keys and accuracy chips;
  floating practice window uses the same scoped colours.
- Slim new concert accent in the independent Performance HUD.
- New offline source regression checks and optional Windows build script.

## Preservation / safety

- The MIDI playback engine, instrument switching code and party-chat protocol are
  byte-for-byte identical to the v0.38.1 package. Existing MIDI files, emote
  routines, saved formations, portraits, snapshots and permissions are not
  migrated or reset.
- The new settings only change how WindboundBard draws its own UI. All ImGui style
  changes are temporary and restored after each panel/window draw.
- No additional game-memory hooks, service calls or network activity.
- Piano Academy still does not send practice notes to FFXIV.
- Showmaster still only runs safe, opted-in routines between performances.
- Existing version-specific source tests may flag renamed version strings or
  navigation being moved to `MainWindow.StageTheme.cs`; these historical tests
  are retained unmodified. Use `Tests/Check-v0.39-Ui-Overhaul.py` for this update.

## Limitations

- No local .NET 10 / Dalamud SDK or running FFXIV client was available to compile,
  render, or test the modified plugin. Run a full **Release | x64** build and
  in-game visual check before publishing or replacing the existing working DLL.
- `Plugin.json`'s existing release download URLs are left unchanged on purpose.
  When publishing v0.39, update your own release manifests after uploading the
  new compiled artifact. This source ZIP does **not** contain compiled release DLLs.
- The new visual-chrome labels are initially in English. Existing translated
  feature controls remain available as before.
