# Windbound Bard v0.37.2 — Full Visual Studio Project

This package contains the complete v0.37.1 project with a dedicated, always-accessible **Band Portraits** tab. It is not a patch or compiled binary.

1. Extract the ZIP into a new folder. Keep your previous working project as a backup.
2. Open `NunuWindboundBard.sln` in Visual Studio; use **Release | x64** and rebuild (requires Dalamud.NET.Sdk 15 and appropriate .NET toolchain).
3. In FFXIV, load the newly built plugin as a Dalamud developer plugin.
4. Open the **Band Portraits** tab (second tab, beside Songs & Playback). It works even if **Solo** is selected and no MIDI file or party is present.
5. Open your own Adventurer Plate or another band member's visible plate, click **CAPTURE** on the matching roster entry, select the portrait square, and **SAVE SELECTED GAME PORTRAIT**.
6. Return to Ensemble track assignments to see the captured thumbnails beside names. The pictures stay on this client; do not expect automatic sharing.

### Source-level regression check

From the project root:

```powershell
python .\Tests\Check-v0.37.2-Band-Portraits-Tab.py
```

This checks tab placement and role independence, but does not compile or test actual game textures. The game capture/renderer must be tested in FFXIV.

### Existing features

Includes v0.37.1 instrument-switch fix, v0.36.1 icon alignment, visual stage formations and auto facing, native ensembles, and Soulweaver Studio. The plugin config is not reset. `Plugin.json` intentionally retains existing release URLs until you publish an actual new package.
