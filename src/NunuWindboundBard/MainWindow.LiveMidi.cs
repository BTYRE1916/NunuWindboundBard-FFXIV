using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    internal bool LiveMidiArmed => outputArmed;

    private void DrawLiveMidiInput()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), Loc.T("LIVE MIDI PIANO — SOLO PERFORMANCE"));
        ImGui.TextWrapped(Loc.T("Play FFXIV Piano live using your USB MIDI keyboard / synthesizer. " +
            "Windbound receives NOTE ON/OFF and sustain (CC64) and sends direct FFXIV 37-key callbacks. " +
            "No MIDI song file needed, no Windows keystrokes and no chat interference."));
        bool solo = (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo;
        if (!solo)
        {
            ImGui.TextColored(new Vector4(1f, .75f, .38f, 1f),
                Loc.T("LIVE MIDI is Solo-only. Select Solo in Ensemble before connecting/playing."));
            return;
        }

        bool live = cfg.LiveMidiInputEnabled;
        if (ImGui.Checkbox(Loc.T("Enable live MIDI controller input##liveMidi"), ref live))
        {
            cfg.LiveMidiInputEnabled = live;
            if (!live) plugin.LiveMidi.Panic();
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Refresh MIDI devices##liveMidi"))) plugin.LiveMidi.RefreshPorts();

        if (plugin.LiveMidi.Ports.Count == 0)
            ImGui.TextDisabled(Loc.T("No MIDI ports listed. Click Refresh after connecting the synth piano."));
        string selected = string.IsNullOrWhiteSpace(cfg.LiveMidiInputDeviceName)
            ? "Choose MIDI keyboard..." : cfg.LiveMidiInputDeviceName;
        if (ImGui.BeginCombo(Loc.T("MIDI input device##liveMidi"), Loc.T(selected)))
        {
            foreach (var port in plugin.LiveMidi.Ports)
            {
                if (ImGui.Selectable(Loc.T($"{port.Name} (port {port.Index})##inPort{port.Index}"),
                    selected == port.Name))
                {
                    cfg.LiveMidiInputDeviceName = port.Name;
                    cfg.Save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.Button(Loc.T(plugin.LiveMidi.Connected ? "Reconnect keyboard##liveMidi" : "Connect keyboard##liveMidi")))
        {
            if (plugin.LiveMidi.Connect(cfg.LiveMidiInputDeviceName, out string error))
            {
                cfg.LiveMidiInputEnabled = true;
                cfg.Save();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Disconnect##liveMidi")))
        {
            plugin.LiveMidi.Disconnect();
            cfg.LiveMidiInputEnabled = false;
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("PANIC: All Notes Off##liveMidi"))) plugin.LiveMidi.Panic();

        bool arm = outputArmed;
        if (ImGui.Checkbox(Loc.T("ARM live notes to FFXIV##liveMidi"), ref arm))
        {
            outputArmed = arm;
            if (!arm) plugin.LiveMidi.Panic();
        }
        ImGui.SameLine();
        ImGui.TextColored(plugin.IsGameOutputSafe() ? new Vector4(.5f, .95f, .6f, 1f) :
            new Vector4(1f, .75f, .38f, 1f),
            Loc.T(plugin.IsGameOutputSafe() ? "37-key Performance READY" : "Open expanded 37-key Performance"));
        if (ImGui.Button(Loc.T("Equip FFXIV Piano (instrument 2)##liveMidi")))
        {
            try
            {
                if (plugin.Instruments.IsReadyFor(2)) { status = "Piano is already ready."; }
                else if (!plugin.Instruments.InProgress)
                {
                    plugin.LiveMidi.Panic();
                    plugin.Instruments.Request(2);
                    status = "Equipping Piano; open the 37-key layout if the UI isn't visible.";
                }
                else status = "An instrument change is already underway; wait for it to finish.";
            }
            catch (Exception ex) { status = "Cannot equip Piano: " + ex.Message; }
        }
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("No loaded MIDI song is necessary."));

        int transpose = cfg.LiveMidiTranspose;
        ImGui.TextUnformatted(Loc.T($"Controller octave transpose: {transpose:+#;-#;0} semitones"));
        if (ImGui.SmallButton(Loc.T("-12##liveTranspose"))) transpose = Math.Max(-48, transpose - 12);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Reset octave##liveTranspose"))) transpose = 0;
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("+12##liveTranspose"))) transpose = Math.Min(48, transpose + 12);
        if (transpose != cfg.LiveMidiTranspose) { cfg.LiveMidiTranspose = transpose; cfg.Save(); }

        bool fold = cfg.LiveMidiFoldToRange;
        if (ImGui.Checkbox(Loc.T("Fold other octaves into C3-C6##liveMidi"), ref fold))
        { cfg.LiveMidiFoldToRange = fold; cfg.Save(); }
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Off = ignore notes outside the 37-key range"));
        bool sustain = cfg.LiveMidiSustain;
        if (ImGui.Checkbox(Loc.T("Use sustain pedal (CC64)##liveMidi"), ref sustain))
        { cfg.LiveMidiSustain = sustain; cfg.Save(); }
        int channel = Math.Clamp(cfg.LiveMidiChannel, 0, 16);
        if (ImGui.BeginCombo(Loc.T("MIDI channel##liveMidi"), Loc.T(channel == 0 ? "All channels (omni)" : $"Channel {channel}")))
        {
            for (int i = 0; i <= 16; i++)
                if (ImGui.Selectable(Loc.T(i == 0 ? "All channels (omni)" : $"Channel {i}"), i == channel))
                { cfg.LiveMidiChannel = i; cfg.Save(); }
            ImGui.EndCombo();
        }
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T(plugin.LiveMidi.Status));
        ImGui.TextUnformatted(Loc.T($"Connected: {(plugin.LiveMidi.Connected ? plugin.LiveMidi.ConnectedName : "No")}" +
            $"   |   Held notes: {plugin.LiveMidi.NotesHeld}" +
            $"   |   Received notes: {plugin.LiveMidi.NotesReceived}"));
        if (plugin.LiveMidi.LastInputNote >= 0)
            ImGui.TextDisabled(Loc.T($"Last MIDI note: {NoteMapper.GetNoteName(plugin.LiveMidi.LastInputNote)} " +
                $"→ Bard: {(plugin.LiveMidi.LastGameNote >= 0 ? NoteMapper.GetNoteName(plugin.LiveMidi.LastGameNote) : "out of range")}"));
        ImGui.TextDisabled(Loc.T($"Out-of-range: {plugin.LiveMidi.OutOfRangeCount}; " +
            $"Driver errors: {plugin.LiveMidi.DriverErrors}; Overflow: {plugin.LiveMidi.OverflowCount}"));
        if (plugin.Player.IsPlaying)
            ImGui.TextColored(new Vector4(1f, .75f, .38f, 1f), Loc.T("MIDI file playback is running. Live keys are safely suspended until the song stops."));
        ImGui.TextDisabled(Loc.T("FFXIV supports only C3-C6. Velocity is not controllable by the in-game keyboard callbacks."));
        ImGui.TextDisabled(Loc.T("Windows MIDI ports (winmm), including Wine only if your Wine MIDI driver exposes the controller. " +
            "One application may have exclusive access to a USB MIDI port."));
    }
}
