# Windbound Bard v0.27.1 — MeltySynth CS1061 compiler hotfix

The preview duration was previously read using the unsupported `MidiFileSequencer.MidiFile` property. MeltySynth 2.4.1 exposes the track duration on the loaded `MidiFile.Length` property instead.

The `AudioPreviewPlayer.PreviewSamples` wrapper now receives the duration when the preview is prepared and returns it without consulting a sequencer property. No playback timing, native MIDI output, ensemble controls or audio rendering logic was changed.

The plugin project version is `0.27.1.0`. A source-level check is included in `Tests/Check-v0.27.1-API.ps1`. Windows/.NET compilation and live FFXIV speaker tests have not been performed here.
