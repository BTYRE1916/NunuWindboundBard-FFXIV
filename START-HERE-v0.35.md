# Nunu's Windbound Bard — v0.35 Full Visual Studio Source

**What this ZIP is:** A full, directly openable source tree (not a patch). It includes `NunuWindboundBard.sln`, the Dalamud API 15 / .NET 10 plugin project, C# sources, tests, samples, localisation and assets. The v0.35 solo-stage and formation automatic-facing changes are **already integrated**.

**Source provenance:** Restored from the last full Windbound source archive (v0.32, German-language release); the core stage, config and plugin files were checked against the current GitHub code. The stage-facing correction and v0.35 integration were applied to this copy. This is a standalone project copy, **not a byte-for-byte export of any customised local `E:\` project**.

## Open and build

1. Extract the ZIP to a **new directory**. Do not merge it over your existing project; that protects local edits and prevents duplicate `MainWindow.SoloStage.cs` declarations.
2. Open **`NunuWindboundBard.sln`** in Visual Studio 2022 with .NET 10 and the correct Dalamud.NET.Sdk workload available.
3. Restore NuGet packages, select **x64** / Debug, then choose **Build > Rebuild Solution**.
4. Alternatively run `Build-Plugin.ps1` from PowerShell in the solution directory.
5. In FFXIV's Dalamud settings (`/xlsettings`), add the built plugin DLL under **Experimental > Dev Plugin Locations** and load the developer plugin.

This package contains **source, not a prebuilt DLL**. A live Dalamud build and in-game facing test could not be run in the packaging environment.

## Solo stage — NO PARTY NEEDED

1. Open Windbound Bard's **Stage Formations** tab. Expand **SOLO STAGE — WALK & FACE (NO PARTY NEEDED)**.
2. Stand near the centre of your stage **looking toward the audience**. Click **MARK / RE-ANCHOR SOLO STAGE AT MY POSITION**.
3. Drag the pink performer marker on the stage diagram, or use the X/depth sliders. Choose the facing angle; **0 degrees means audience-forward** relative to the saved centre heading.
4. Choose **WALK TO SOLO SPOT & FACE**. With **vnavmesh installed on that client**, the character walks to the stage spot, then automatically turns.
5. Choose **FACE FORWARD NOW** to turn to the original stage-forward direction without walking; it does not need vnavmesh. Use **STOP SOLO STAGE MOVEMENT** at any time.

Solo stage movement is never triggered by MIDI loading, login, song selection or automatic playback. The character must not be performing or in combat.

## Party / ensemble formation

1. Each follower selects native Ensemble **Follower** and explicitly checks **Allow my party leader to save my position and move me on stage**.
2. For each client, enable **Automatically face saved direction after walking** (on by default in this source) to turn after arrival. The leader has a separate local toggle.
3. The conductor enters **Stage Formations**, optionally enables **Visual Stage Designer**, creates/loads the layout and assigns Bards to spots.
4. Adjust individual facing angles or choose **ALL FACE AUDIENCE**; then **SET POSITIONS & MOVE PARTY**. Once walking and turning finish, the app acknowledges the performer is positioned.
5. **STOP STAGE MOVEMENT** cancels movement and turning without stopping or starting music.

Walking requires vnavmesh on each moving client. Follower controls require user consent, a matching party roster, the same territory and a nearby party leader. The game client and plugin versions must be mutually compatible.

## Files specifically repaired / added

* `src/NunuWindboundBard/MainWindow.SoloStage.cs` — solo-stage UI and walk/face logic (new)
* `src/NunuWindboundBard/Core/SoloStageLayout.cs` — persisted solo layout (new)
* `src/NunuWindboundBard/Core/StageFacingController.cs` — bounded native auto-facing requests (new)
* `src/NunuWindboundBard/MainWindow.StageFormations.cs` — shared facing, arrival checks, stop/recover and solo UI hookup
* `src/NunuWindboundBard/MainWindow.StageDesigner.cs` — facing arrows and Face Audience action
* `src/NunuWindboundBard/Configuration.cs` — `SoloStage` and `AutoFaceAfterStageMovement` settings
* `src/NunuWindboundBard/Plugin.cs` — missing `IGameConfig` Dalamud service and version migration
* `src/NunuWindboundBard/NunuWindboundBard.csproj` — v0.35 assembly version

## Validation

Run `py Tests\Check-v0.35-Solo-Stage-and-AutoFacing.py` to perform **source-only** safety/integration checks. The legacy tests titled v0.18–v0.32 are historical and some intentionally assert a previous, exact assembly version. They are not v0.35 release validation.

**Important:** Automatic turning uses the native game action manager, which can change when FFXIV updates. Stop and disable the feature if game behaviour is unexpected. The live client is the final test.
