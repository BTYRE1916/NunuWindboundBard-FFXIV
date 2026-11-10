# Windbound Bard v0.39 — Build and explore

This package is a **complete Visual Studio solution** built from the v0.38.1
Full Emote Library project, with a UI-focused v0.39 update. It has *not*
been run in-game or compiled against a local Dalamud SDK in this environment.

## Build

1. Unzip the folder somewhere new. Keep your working v0.38.1 build as backup.
2. Open `NunuWindboundBard.sln` in Visual Studio on Windows.
3. Ensure the Dalamud.NET.Sdk/15.0.0 and .NET 10 dependencies are available.
4. Select **Release** configuration and **x64** platform.
5. Choose **Build > Rebuild Solution**.
6. Load the newly produced plugin DLL through Dalamud's Developer Plugins UI.
   Do not overwrite a working live installation until in-game testing succeeds.

Offline source checks (from the project folder):

```powershell
py -3 .\Tests\Check-v0.39-Ui-Overhaul.py
```

Optional validation/build helper:

```powershell
.\Tests\Run-v0.39-Tests.ps1 -Build
```

## Navigation

| Workspace | Views |
|---|---|
| PERFORMANCE | Songs & Playback, Instruments, Live MIDI Piano, Guitar Tones |
| BAND | Band Portraits, Ensemble, Stage Formations, Concert Snapshots |
| STUDIO | Piano Academy, Soulweaver Studio, Audio Preview, MIDI Inspector, FFXIV Optimiser |
| SHOW | Showmaster |
| TOOLS | Direct Output & Test |
| SETUP | Appearance, About / Setup |

The Band Portraits editor remains accessible in Solo mode and its capture window
remains open even when changing workspaces. Piano Academy's floating practice view
is independent of the main plugin window as before.

## Appearance

Go to **SETUP > Appearance** to select:

- **Enable Dark Stage styling** (on by default): enable the rose/violet stage UI.
- **Accent colour**: Soul Rose, Void Violet or Aether Cyan.
- **Subtle animated concert highlight**: disable for a completely still header.
- The header's **Plain colours** button returns to normal Dalamud colours while
  keeping the tidier new workspace navigation.

The chips at the top show status only. They do NOT automatically ARM game notes
or change instruments. The existing ARM/playback buttons remain in their normal
feature pages; Showmaster retains independent follower consent.

## First smoke test

1. Launch the UI; confirm all six workspaces and sub-views are visible.
2. Switch between Rose, Violet and Cyan; verify other Dalamud windows are unaffected.
3. Toggle **Plain colours**, then **Dark Stage UI**; ensure buttons still respond.
4. Load a small MIDI, verify the song name in the header, try transport in safe
   preview mode, then use a local piano lesson.
5. Open Band Portraits, the Stage Formations designer and Showmaster.
6. Open the independent HUD and large Piano Academy window; close them normally.
7. Only after that verify the sensitive Performance output, native instrument
   switching, group ensemble and Stage/Formations functions in-game.

If you see a runtime exception or ImGui assertion after upgrading, return to the
known working build and share the Visual Studio / Dalamud log with the failing
method name. Note the new workspaces reside in `MainWindow.StageTheme.cs`.

## Important

`Plugin.json` still references the previously published release. Update your own
GitHub release and plugin-manifest URLs when **you** publish a new build. No fork,
push or upstream change is performed by this package.
