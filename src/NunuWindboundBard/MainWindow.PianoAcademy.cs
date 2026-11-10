using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>Full-range, local-only piano lesson display. Never submits FFXIV notes.</summary>
public sealed partial class MainWindow
{
    private readonly PianoAcademyEngine academy = new();
    private MidiSong? academySource;
    private int academyTrack = -1;
    private PianoPracticeHand academyHand = PianoPracticeHand.Both;
    private bool academyMonitor;
    private bool academyFloating;
    private string academyStatus = "Choose a MIDI and connect a USB piano to practise.";
    private long academyPreviousQpc;
    private int academyKeyboardStart = 21;
    private int academyKeyboardKeys = 88;
    internal bool PianoAcademyMonitoring => academyMonitor;

    internal void OnAcademyMidi(uint raw)
    {
        if (!academyMonitor) return;
        int status = (int)(raw & 0xFF);
        int eventType = status & 0xF0;
        if ((status & 0x0F) == 9) return; // Ignore the percussion channel.
        int pitch = (int)((raw >> 8) & 0x7F);
        int velocity = (int)((raw >> 16) & 0x7F);
        if (eventType == 0x90 && velocity != 0) academy.NoteOn(pitch, velocity);
        else if (eventType is 0x80 or 0x90) academy.NoteOff(pitch);
    }

    public void PollPianoAcademy()
    {
        if (!ReferenceEquals(academySource, song)) ResetAcademyLesson();
        plugin.LiveMidi.PracticeMonitoring = academyMonitor && academy.HasMidi;
        long now = Stopwatch.GetTimestamp();
        if (academyPreviousQpc != 0)
            academy.Advance((now - academyPreviousQpc) * 1000.0 / Stopwatch.Frequency);
        academyPreviousQpc = now;
        if (academy.Running && plugin.Player.IsPlaying)
        {
            academy.Pause();
            academyStatus = "Practice paused because FFXIV MIDI playback started.";
        }
    }

    private void ResetAcademyLesson()
    {
        if (song is null) { academySource = null; academy.Clear(); return; }
        academySource = song;
        academy.Load(song, academyTrack, academyHand, cfg.PianoAcademySplitPitch);
        academy.Speed = cfg.PianoAcademySpeed;
        academyStatus = academy.Notes.Count == 0
            ? "This track/hand has no piano notes in A0-C8. Try All Tracks / Both Hands."
            : $"Lesson loaded with {academy.Notes.Count:N0} notes. Your keyboard supplies its own sound.";
    }

    private void DrawPianoAcademy()
    {
        ImGui.TextColored(new Vector4(.95f,.57f,.83f,1f), "WINDBOUND PIANO ACADEMY — FALLING-NOTE TUTOR");
        ImGui.TextWrapped("Practise the ORIGINAL full-range MIDI with your real synthesizer. " +
            "This tutor never sends notes to FFXIV or party chat. Your piano plays its own sound.");
        ImGui.TextDisabled("Load a song in Songs & Playback, or browse here. Format 0/1 MIDI, all A0-C8 keys supported.");
        if (ImGui.Button("Browse lesson MIDI...##academy"))
            fileDialogs.OpenFileDialog("Piano Academy - Open MIDI", MidiFilter,
                (ok, paths) => { if (ok && paths.Count > 0 && !plugin.Player.IsPlaying) LoadFromUser(paths[0]); },
                1, GetFileDialogStartFolder());
        ImGui.SameLine();
        if (!string.IsNullOrEmpty(loaded)) ImGui.TextDisabled(Path.GetFileName(loaded));
        if (!ReferenceEquals(academySource, song)) ResetAcademyLesson();
        if (song is null) { ImGui.TextWrapped("Load a MIDI to begin learning."); return; }

        string laneName = academyTrack == -1 ? "All non-percussion tracks" :
            song.Tracks.FirstOrDefault(t => t.Index == academyTrack)?.Name ?? "Track not found";
        ImGui.SetNextItemWidth(270);
        if (ImGui.BeginCombo("Practice MIDI track##academy", laneName))
        {
            if (ImGui.Selectable("All non-percussion tracks", academyTrack == -1))
            { academyTrack = -1; ResetAcademyLesson(); }
            foreach (var lane in song.Tracks.Where(t => t.NoteCount > 0 && !t.IsPercussion))
                if (ImGui.Selectable($"{lane.Index + 1}: {lane.Name}##academy{lane.Index}", academyTrack == lane.Index))
                { academyTrack = lane.Index; ResetAcademyLesson(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        string handLabel = academyHand switch {
            PianoPracticeHand.LowerNotes => "Lower notes (pink)",
            PianoPracticeHand.UpperNotes => "Upper notes (blue)", _ => "Both hands"
        };
        ImGui.SetNextItemWidth(215);
        if (ImGui.BeginCombo("Practice range##academy", handLabel))
        {
            foreach (var kind in Enum.GetValues<PianoPracticeHand>())
                if (ImGui.Selectable(kind.ToString(), academyHand == kind))
                { academyHand = kind; ResetAcademyLesson(); }
            ImGui.EndCombo();
        }
        int split = cfg.PianoAcademySplitPitch;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderInt("Lower / upper split (MIDI key)##academy", ref split, 36, 84))
        { cfg.PianoAcademySplitPitch = split; cfg.Save(); ResetAcademyLesson(); }

        int mode = (int)academy.Mode;
        if (ImGui.RadioButton("Wait for correct notes##academy", mode == 0))
        { academy.Mode = PianoPracticeMode.WaitForNotes; academy.Rewind(); }
        ImGui.SameLine();
        if (ImGui.RadioButton("Play Along (timing score)##academy", mode == 1))
        { academy.Mode = PianoPracticeMode.PlayAlong; academy.Rewind(); }
        float speed = academy.Speed;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Lesson speed##academy", ref speed, .25f, 2.0f, "%.2fx"))
        { academy.Speed = speed; cfg.PianoAcademySpeed = speed; cfg.Save(); }

        ImGui.Separator();
        if (ImGui.Button(academy.Running ? "PAUSE LESSON##academy" : "PLAY / RESUME LESSON##academy"))
        {
            if (plugin.Player.IsPlaying) academyStatus = "Stop FFXIV MIDI playback before starting a piano lesson.";
            else { academy.Play(); academyStatus = "Lesson running. Play the falling notes on your synth piano."; }
        }
        ImGui.SameLine();
        if (ImGui.Button("RESTART##academy"))
        {
            if (plugin.Player.IsPlaying) academyStatus = "Stop FFXIV MIDI playback before restarting the lesson.";
            else { academy.Rewind(); academy.Play(); }
        }
        ImGui.SameLine();
        if (ImGui.Button("STOP##academy")) academy.Stop();
        float progress = (float)Math.Clamp(academy.PositionMs / Math.Max(1, academy.DurationMs), 0, 1);
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{academy.PositionMs/1000:0.0}s / {academy.DurationMs/1000:0.0}s");

        bool loop = academy.LoopEnabled;
        if (ImGui.Checkbox("Loop practise section (Play Along mode)##academy", ref loop))
        {
            academy.LoopEnabled = loop;
            if (loop && academy.LoopEndMs <= academy.LoopStartMs + 100)
            { academy.LoopStartMs = 0; academy.LoopEndMs = academy.DurationMs; }
        }
        if (loop)
        {
            float a = (float)(academy.LoopStartMs / 1000);
            float b = (float)(academy.LoopEndMs <= a * 1000 ? academy.DurationMs / 1000 : academy.LoopEndMs / 1000);
            float max = (float)Math.Max(1, academy.DurationMs / 1000);
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Loop start (s)##academy", ref a, 0, max, "%.1f"))
                academy.LoopStartMs = a * 1000;
            ImGui.SetNextItemWidth(190);
            if (ImGui.SliderFloat("Loop end (s)##academy", ref b, 0, max, "%.1f"))
                academy.LoopEndMs = Math.Min(academy.DurationMs, Math.Max(academy.LoopStartMs + 100, b * 1000));
        }
        ImGui.Separator();
        bool monitor = academyMonitor;
        if (ImGui.Checkbox("Read my USB MIDI keyboard for lessons (no FFXIV output)##academy", ref monitor))
        {
            academyMonitor = monitor;
            if (!monitor) academy.AllNotesOff();
            plugin.LiveMidi.PracticeMonitoring = monitor;
        }
        if (ImGui.SmallButton("Refresh MIDI ports##academy")) plugin.LiveMidi.RefreshPorts();
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.LiveMidi.Connected ? $"Connected: {plugin.LiveMidi.ConnectedName}" : "No MIDI keyboard connected");
        string deviceName = cfg.LiveMidiInputDeviceName;
        ImGui.SetNextItemWidth(270);
        if (ImGui.BeginCombo("MIDI keyboard##academy", string.IsNullOrEmpty(deviceName) ? "Choose USB piano" : deviceName))
        {
            foreach (var p in plugin.LiveMidi.Ports)
                if (ImGui.Selectable(p.Name + "##acad-port" + p.Index, deviceName == p.Name))
                { cfg.LiveMidiInputDeviceName = p.Name; cfg.Save(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("CONNECT##academy"))
        {
            if (plugin.LiveMidi.Connect(cfg.LiveMidiInputDeviceName, out string error))
            { academyMonitor = true; plugin.LiveMidi.PracticeMonitoring = true; academyStatus = "Piano connected for practice (no FFXIV note output)."; }
            else academyStatus = error;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Disconnect##academy"))
        { academyMonitor = false; plugin.LiveMidi.PracticeMonitoring = false; plugin.LiveMidi.Disconnect(); academy.AllNotesOff(); }
        ImGui.TextDisabled("The Live MIDI Piano tab shares this port. Practice monitoring temporarily suspends live FFXIV note output.");
        if (ImGui.Button(academyFloating ? "CLOSE LARGE PRACTICE VIEW##academy" : "OPEN LARGE PRACTICE VIEW##academy"))
            academyFloating = !academyFloating;

        ImGui.TextUnformatted($"Hits: {academy.Hits}   Missed: {academy.Misses}   Wrong keys: {academy.WrongKeys}   Accuracy: {academy.AccuracyPercent:0.0}%");
        if (academy.Mode == PianoPracticeMode.WaitForNotes && academy.ExpectedPitches.Count > 0)
            ImGui.TextWrapped("Next chord: " + string.Join("  ", academy.ExpectedPitches.Select(NoteMapper.GetNoteName)));
        if (academy.Finished) ImGui.TextColored(new Vector4(.56f,.92f,.67f,1f), "Lesson complete! Brilliant work!");
        ImGui.TextWrapped(academyStatus);

        ImGui.Separator();
        int keys = academyKeyboardKeys;
        ImGui.SetNextItemWidth(190);
        if (ImGui.SliderInt("Visible keyboard keys##academy", ref keys, 37, 88)) academyKeyboardKeys = keys;
        academyKeyboardStart = Math.Clamp(academyKeyboardStart, 21, 109 - academyKeyboardKeys);
        int start = academyKeyboardStart;
        ImGui.SetNextItemWidth(190);
        if (ImGui.SliderInt("Keyboard starts at MIDI pitch##academy", ref start, 21, 109 - keys))
            academyKeyboardStart = start;
        DrawAcademyPianoRoll(academyKeyboardStart, academyKeyboardKeys);
    }

    public void DrawAcademyFloatingWindow()
    {
        if (!academyFloating) return;
        ImGui.SetNextWindowSize(new Vector2(1000, 560), ImGuiCond.FirstUseEver);
        bool visible = ImGui.Begin("Windbound Piano Academy — Practice View###WindboundPianoAcademy",
            ref academyFloating, ImGuiWindowFlags.NoCollapse);
        try
        {
            if (!visible) return;
            ImGui.TextColored(new Vector4(.95f,.57f,.83f,1f), "PIANO ACADEMY — LARGE FALLING-NOTE PRACTICE VIEW");
            ImGui.TextDisabled(academyMonitor && plugin.LiveMidi.Connected
                ? $"Hardware MIDI connected: {plugin.LiveMidi.ConnectedName} (local-only)"
                : "Connect a MIDI keyboard in the Piano Academy tab; this window never sends FFXIV notes.");
            if (academySource is null || !academy.HasMidi)
            { ImGui.TextWrapped("Load a MIDI on the Piano Academy tab first."); return; }
            ImGui.TextUnformatted($"{Path.GetFileName(loaded)} | {academy.PositionMs/1000:0.0}s / {academy.DurationMs/1000:0.0}s | " +
                $"Hits {academy.Hits} | Missed {academy.Misses} | Accuracy {academy.AccuracyPercent:0.0}%");
            if (ImGui.Button(academy.Running ? "Pause##floatacad" : "Play / Resume##floatacad"))
            {
                if (academy.Running) academy.Pause();
                else if (!plugin.Player.IsPlaying) academy.Play();
            }
            ImGui.SameLine();
            if (ImGui.Button("Restart##floatacad") && !plugin.Player.IsPlaying)
            { academy.Rewind(); academy.Play(); }
            ImGui.SameLine();
            if (ImGui.Button("Stop##floatacad")) academy.Stop();
            ImGui.SameLine();
            if (academy.Mode == PianoPracticeMode.WaitForNotes && academy.ExpectedPitches.Count > 0)
                ImGui.TextUnformatted("Play: " + string.Join(" + ", academy.ExpectedPitches.Select(NoteMapper.GetNoteName)));
            DrawAcademyPianoRoll(academyKeyboardStart, academyKeyboardKeys);
        }
        finally { ImGui.End(); }
    }

    private static bool AcademyBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;

    private static float AcademyKeyX(int pitch, int firstPitch, float whiteWidth)
    {
        int whites = 0;
        for (int p = firstPitch; p < pitch; p++) if (!AcademyBlack(p)) whites++;
        return whites * whiteWidth + (AcademyBlack(pitch) ? -whiteWidth * .18f : whiteWidth * .5f);
    }

    private void DrawAcademyPianoRoll(int firstPitch, int count)
    {
        const float whiteWidth = 18f, rollHeight = 262f, keyHeight = 62f;
        int lastPitch = Math.Min(108, firstPitch + count - 1);
        int whites = Enumerable.Range(firstPitch, lastPitch - firstPitch + 1).Count(p => !AcademyBlack(p));
        float width = whites * whiteWidth + 18f;
        ImGui.TextDisabled("Pink = lower notes  |  Blue = upper notes  |  Green = pressed  |  Golden line = play-now");
        // Horizontal scrollbar is important for the full 88 keys on smaller displays.
        ImGui.BeginChild("##WindboundAcademyScroll", new Vector2(0, 352), true, ImGuiWindowFlags.HorizontalScrollbar);
        Vector2 top = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##AcademyKeyboardCanvas", new Vector2(width, rollHeight + keyHeight + 4f));
        var draw = ImGui.GetWindowDrawList();
        float lineY = top.Y + rollHeight;
        draw.AddRectFilled(top, top + new Vector2(width, rollHeight), 0xFF161625);
        for (int pitch = firstPitch; pitch <= lastPitch; pitch++)
        {
            if (AcademyBlack(pitch)) continue;
            float x = top.X + AcademyKeyX(pitch, firstPitch, whiteWidth) - whiteWidth * .5f;
            draw.AddLine(new Vector2(x, top.Y), new Vector2(x, lineY), 0x243D3C55);
            if (pitch % 12 == 0)
                draw.AddText(new Vector2(x+2, top.Y+4), 0xFFC6C2DA, $"C{pitch/12-1}");
        }
        foreach (var note in academy.VisibleNotes())
        {
            if (note.Pitch < firstPitch || note.Pitch > lastPitch) continue;
            double distanceMs = note.StartMs - academy.PositionMs;
            if (distanceMs is > 3100 or < -850) continue;
            float x = top.X + AcademyKeyX(note.Pitch, firstPitch, whiteWidth);
            float center = lineY - (float)(distanceMs / 3100.0 * (rollHeight-12));
            float duration = (float)(Math.Min(1400, note.DurationMs) / 3100 * (rollHeight-12));
            float y1 = Math.Max(top.Y+1, center-duration);
            float y2 = Math.Min(lineY-1, center);
            if (y2 < y1) continue;
            uint colour = note.Missed ? 0xFF555566 : note.Hit ? 0xFF50CF92 :
                note.Pitch < cfg.PianoAcademySplitPitch ? 0xFFE878B2 : 0xFFFFBE64;
            float half = AcademyBlack(note.Pitch) ? 6f : 8f;
            draw.AddRectFilled(new Vector2(x-half,y1), new Vector2(x+half,y2), colour, 3);
        }
        draw.AddLine(new Vector2(top.X,lineY),new Vector2(top.X+width,lineY),0xFFFFD789,3f);
        // White keys first, then black keys overlaid for a real piano layout.
        for (int p = firstPitch; p <= lastPitch; p++)
        {
            if (AcademyBlack(p)) continue;
            float x = top.X + AcademyKeyX(p, firstPitch, whiteWidth) - whiteWidth*.5f;
            bool down = academy.IsHeld(p);
            draw.AddRectFilled(new Vector2(x,lineY+2),new Vector2(x+whiteWidth-1,lineY+keyHeight),
                down ? 0xFF50CF92 : 0xFFF4F0F0, 2f);
            if (p % 12 == 0) draw.AddText(new Vector2(x+2,lineY+keyHeight-17),0xFF38333D,$"C{p/12-1}");
        }
        for (int p = firstPitch; p <= lastPitch; p++)
        {
            if (!AcademyBlack(p)) continue;
            float x = top.X + AcademyKeyX(p, firstPitch, whiteWidth);
            draw.AddRectFilled(new Vector2(x-6,lineY+2),new Vector2(x+6,lineY+keyHeight*.63f),
                academy.IsHeld(p) ? 0xFF50CF92 : 0xFF262330, 2f);
        }
        ImGui.EndChild();
    }
}
