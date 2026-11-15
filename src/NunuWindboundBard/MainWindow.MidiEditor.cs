using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private readonly MidiEditorDevices editorDevices = new();
    private MidiEditorDocument? editor;
    private readonly HashSet<int> editorSelection = new(), editorHiddenTracks = new(), editorHiddenChannels = new(),
        editorMutedTracks = new(), editorMutedChannels = new();
    private readonly List<string> editorRecents = new();
    private IReadOnlyList<WinMmMidiInput.Port> editorInputs = [];
    private string[] editorOutputs = [];
    private string editorStatus = "Create a blank MIDI or open an existing file.";
    private string editorSavePath = "", editorTrackName = "", editorFont = "";
    private int editorTrack, editorChannel = 1, editorSoloTrack, editorSoloChannel, editorSavedRevision;
    private long editorCursor, editorViewStart, editorPlayStart, editorRecordStart;
    private float editorSpeed = 1;
    private bool editorPreview, editorExternal, editorExternalPaused, editorClick, editorFollow = true, editorLargeTransport;
    private bool editorQueuedRecord, editorOverdub, editorRecordNotes = true, editorRecordControls = true;
    private bool editorDeviceRestoreAttempted;
    private int editorRecordChannel;
    private List<EditorEvent>? editorTake;
    private Task<EditorJob>? editorJob;
    private Action? editorDiscardAction;
    private sealed record EditorJob(string Message, MidiEditorDocument? Opened = null, string Saved = "",
        int Revision = 0, byte[]? Preview = null, string Font = "", bool Workbench = false);

    private void LoadEditorPreferences()
    {
        if (cfg.MidiEditorRecentFiles is not null)
            editorRecents.AddRange(cfg.MidiEditorRecentFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Take(10));
        editorDevices.ExplicitNoteOffs = cfg.MidiEditorExplicitNoteOffs;
    }

    private void EditorDeviceAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { editorStatus = "MIDI device: " + ex.Message; editorDevices.ReleaseDevicesAfterError(); }
    }

    private void RestoreEditorDevices()
    {
        editorDeviceRestoreAttempted = true;
        if (!cfg.MidiEditorRememberDevices || EditorGameBusy) return;
        EditorDeviceAction(() =>
        {
            editorInputs = MidiEditorDevices.Inputs();
            editorOutputs = MidiEditorDevices.Outputs();
            var input = editorInputs.FirstOrDefault(p => p.Name == cfg.MidiEditorInputDevice);
            if (input is not null) editorDevices.OpenInput(input);
            int output = Array.IndexOf(editorOutputs, cfg.MidiEditorOutputDevice);
            if (output >= 0) editorDevices.OpenOutput(output);
        });
    }

    private void QueueEditor(Func<EditorJob> action)
    {
        if (editorJob is not null) return;
        editorStatus = "Working…";
        editorJob = Task.Run(action);
        _ = editorJob.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private bool EditorBusy => editorJob is not null || editorDevices.Recording || editorQueuedRecord || editorTake is not null;
    private bool EditorGameBusy => plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending;
    private bool EditorPlaying => editorDevices.Playing || editorPreview && (plugin.AudioPreview.IsLoading || plugin.AudioPreview.IsPlaying);

    internal void PollMidiEditor()
    {
        try
        {
            if (editorJob is { IsCompleted: true })
            {
                var task = editorJob;
                editorJob = null;
                var result = task.GetAwaiter().GetResult();
                if (result.Opened is not null) SetEditor(result.Opened);
                if (result.Saved.Length > 0) { editorSavePath = result.Saved; editorSavedRevision = result.Revision; }
                if (result.Workbench) ImportWorkbench(result.Saved);
                if (result.Preview is not null)
                {
                    if (EditorGameBusy) throw new InvalidOperationException("Stop game playback before starting the MIDI editor preview.");
                    plugin.AudioPreview.SetVolume(cfg.PreviewVolume);
                    plugin.AudioPreview.SetSpeed(editorSpeed);
                    plugin.AudioPreview.Start("Windbound MIDI Editor.mid", result.Font, result.Preview);
                    editorPreview = true;
                }
                editorStatus = result.Message;
            }
            if (EditorGameBusy && (EditorPlaying || editorDevices.Recording || editorDevices.Thru))
            {
                if (editorDevices.Recording) FinishEditorRecording();
                editorDevices.SetThru(false);
                StopEditorPlayback();
                editorStatus = "Local MIDI output stopped for game playback.";
            }
            if (editorQueuedRecord && editorPreview && plugin.AudioPreview.IsPlaying)
            {
                editorRecordStart = EditorPlaybackCursor();
                editorDevices.StartRecording();
                editorQueuedRecord = false;
            }
            long playhead = EditorPlaybackCursor();
            bool active = editorDevices.Playing || editorPreview && plugin.AudioPreview.IsPlaying;
            editorDevices.Update();
            if (editorPreview && (!plugin.AudioPreview.IsPlaying && !plugin.AudioPreview.IsLoading ||
                                  plugin.AudioPreview.CurrentSong != "Windbound MIDI Editor.mid"))
            {
                editorPreview = false;
                if (editorQueuedRecord) throw new InvalidOperationException("The backing preview did not start. " + plugin.AudioPreview.Status);
            }
            if (active && editorFollow && !editorRollDragging)
            {
                if (playhead < editorViewStart || playhead >= editorViewStart + EditorSpan)
                    editorViewStart = Math.Max(0, playhead - EditorSpan / 8);
            }
            if (editorDevices.Recording && editorDevices.TakeOverflowed)
            {
                FinishEditorRecording();
                editorStatus = "MIDI input reached its buffer limit. Review the captured take before keeping it.";
            }
        }
        catch (Exception ex)
        {
            editorDevices.CancelRecording();
            editorDevices.ReleaseDevicesAfterError();
            StopEditorPlayback();
            editorStatus = "MIDI Editor: " + ex.Message;
        }
    }

    private void SetEditor(MidiEditorDocument document)
    {
        StopEditorPlayback();
        editor = document;
        editorSavedRevision = document.Revision;
        editorSavePath = "";
        editorSelection.Clear();
        editorHiddenTracks.Clear(); editorHiddenChannels.Clear(); editorMutedTracks.Clear(); editorMutedChannels.Clear();
        editorSoloTrack = editorSoloChannel = 0;
        editorCursor = editorViewStart = 0;
        editorSelectedEventId = editorEventPage = 0;
        editorRollDragging = false;
        editorCurveStroke.Clear();
        SetEditorTrack(document.State.Tracks[0]);
        if (document.Source.Length > 0)
        {
            editorRecents.RemoveAll(path => string.Equals(path, document.Source, StringComparison.OrdinalIgnoreCase));
            editorRecents.Insert(0, document.Source);
            if (editorRecents.Count > 10) editorRecents.RemoveAt(10);
            cfg.MidiEditorRecentFiles = editorRecents.ToList();
            cfg.Save();
        }
    }

    private void SetEditorTrack(EditorTrack track) { editorTrack = track.Id; editorTrackName = track.Name; }
    private void RequestEditorDocument(Action open)
    {
        if (editor is not null && editor.Revision != editorSavedRevision)
        {
            editorDiscardAction = open;
            ImGui.OpenPopup("Discard unsaved MIDI edits?##editor");
        }
        else open();
    }

    private void OpenEditor(string path) => QueueEditor(() => new("MIDI opened into the editor.", Opened: MidiEditorDocument.Open(path)));

    private void SaveEditor(bool saveAs, bool asWorkbench = false)
    {
        var document = editor!;
        void Save(string path, bool replace)
        {
            var snapshot = document.State;
            int revision = document.Revision;
            QueueEditor(() =>
            {
                document.Save(snapshot, path, replace);
                return new("MIDI saved: " + path, Saved: path, Revision: revision, Workbench: asWorkbench);
            });
        }
        if (!saveAs && editorSavePath.Length > 0) { Save(editorSavePath, true); return; }
        string stem = document.Source.Length == 0 ? "Untitled" : Path.GetFileNameWithoutExtension(document.Source);
        fileDialogs.SaveFileDialog("Save edited MIDI", MidiFilter, stem + " - Edited.mid", ".mid",
            (ok, path) => { if (ok && !EditorBusy) Save(path, false); }, GetFileDialogStartFolder());
    }

    private void ApplyEditor(Action change, bool retainSelection = true)
    {
        try
        {
            StopEditorPlayback();
            change();
            if (retainSelection)
            {
                var active = editor!.State.Events.Select(e => e.Id).ToHashSet();
                editorSelection.RemoveWhere(id => !active.Contains(id));
            }
            else editorSelection.Clear();
            if (!editor!.State.Tracks.Any(t => t.Id == editorTrack)) SetEditorTrack(editor.State.Tracks[0]);
            if (editorSoloTrack != 0 && !editor.State.Tracks.Any(t => t.Id == editorSoloTrack)) editorSoloTrack = 0;
            editorStatus = $"{editor.State.Tracks.Length} tracks · {editor.State.Events.Length:N0} events";
        }
        catch (Exception ex) { editorStatus = "MIDI Editor: " + ex.Message; }
    }

    private void StartEditorPlayback(bool record = false)
    {
        if (EditorGameBusy) { editorStatus = "Stop game playback before using local MIDI playback."; return; }
        var doc = editor!;
        StopEditorPlayback();
        editorPlayStart = Math.Clamp(editorCursor, 0, doc.State.EndTick);
        var filtered = MidiEditorPlayback.FromCursor(doc.State, doc.Clock, editorPlayStart,
            editorMutedTracks, editorMutedChannels, editorSoloTrack, editorSoloChannel, editorClick);
        if (editorExternal)
        {
            try
            {
                editorDevices.Play(doc, filtered, editorSpeed);
                if (record) { editorRecordStart = editorPlayStart; editorDevices.StartRecording(); }
                editorStatus = "Playing through " + editorDevices.OutputName;
            }
            catch (Exception ex) { StopEditorPlayback(); editorStatus = ex.Message; }
        }
        else
        {
            string font = string.IsNullOrWhiteSpace(editorFont) ? cfg.PreviewSoundFontPath ?? "" : editorFont;
            if (!File.Exists(font)) { editorStatus = "Choose a SoundFont (.sf2) or select a MIDI output device."; return; }
            editorQueuedRecord = record;
            QueueEditor(() => new("Playing local SoundFont preview.", Preview: MidiEditorPlayback.PreviewBytes(doc, filtered), Font: font));
        }
    }

    private long EditorPlaybackCursor()
    {
        if (editor is null || !EditorPlaying) return editorCursor;
        double position = editorDevices.Playing ? editorDevices.Position : plugin.AudioPreview.Position.TotalMilliseconds;
        return editor.Clock.Ticks(editor.Clock.Milliseconds(editorPlayStart) + position);
    }

    private void StopEditorPlayback()
    {
        editorQueuedRecord = false;
        editorExternalPaused = false;
        editorDevices.StopPlayback();
        if (editorPreview)
        {
            if (plugin.AudioPreview.CurrentSong == "Windbound MIDI Editor.mid") plugin.AudioPreview.Stop("MIDI Editor preview stopped.");
            editorPreview = false;
        }
    }

    private void StartEditorRecording()
    {
        if (EditorGameBusy) { editorStatus = "Stop game playback before recording MIDI."; return; }
        if (!editorDevices.InputConnected) { editorStatus = "Connect a MIDI input in MIDI devices."; return; }
        if (editorOverdub) { StartEditorPlayback(true); return; }
        StopEditorPlayback();
        editorRecordStart = editorCursor;
        editorDevices.StartRecording();
        editorStatus = "Recording MIDI…";
    }

    private void FinishEditorRecording()
    {
        editorDevices.Update();
        var take = editorDevices.FinishRecording(editor!, editorRecordStart, editorTrack, 0, editorOverdub ? editorSpeed : 1);
        StopEditorPlayback();
        editorTake = take.Count == 0 ? null : take;
        editorStatus = take.Count == 0 ? "No MIDI events were captured." : $"Captured {take.Count:N0} events. Review the take below.";
    }

    private void DrawMidiEditor()
    {
        if (!editorDeviceRestoreAttempted) RestoreEditorDevices();
        ImGui.BeginDisabled(EditorBusy);
        if (ImGui.Button("New MIDI##editor")) RequestEditorDocument(() => SetEditor(MidiEditorDocument.New()));
        ImGui.SameLine();
        if (ImGui.Button("Open MIDI…##editor")) RequestEditorDocument(() => fileDialogs.OpenFileDialog("Open in MIDI Editor", MidiFilter,
            (ok, paths) => { if (ok && paths.Count > 0) OpenEditor(paths[0]); }, 1, GetFileDialogStartFolder()));
        ImGui.SameLine();
        ImGui.BeginDisabled(editorRecents.Count == 0);
        if (ImGui.BeginCombo("##editorRecents", "Recent files"))
        {
            foreach (var path in editorRecents.ToArray())
                if (ImGui.Selectable(Path.GetFileName(path) + "##" + path)) RequestEditorDocument(() => OpenEditor(path));
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (editor is not null)
        {
            if (ImGui.Button("Save##editor")) SaveEditor(false);
            ImGui.SameLine(); if (ImGui.Button("Save as…##editor")) SaveEditor(true);
            ImGui.SameLine(); if (ImGui.Button("Save and open in Workbench…##editor")) SaveEditor(true, true);
        }
        ImGui.EndDisabled();
        if (ImGui.BeginPopupModal("Discard unsaved MIDI edits?##editor"))
        {
            ImGui.TextUnformatted("Save the current MIDI before discarding it if you want to keep your edits.");
            if (ImGui.Button("Discard and continue"))
            {
                var action = editorDiscardAction; editorDiscardAction = null;
                ImGui.CloseCurrentPopup(); action?.Invoke();
            }
            ImGui.SameLine();
            if (ImGui.Button("Keep editing")) { editorDiscardAction = null; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        ImGui.TextWrapped(editorStatus);
        if (editor is null) return;
        ImGui.TextUnformatted((editorSavePath.Length > 0 ? Path.GetFileName(editorSavePath) :
            editor.Source.Length > 0 ? Path.GetFileName(editor.Source) : "Untitled MIDI") +
            (editor.Revision != editorSavedRevision ? " *" : ""));
        DrawEditorTransport();
        ImGui.BeginDisabled(EditorBusy);
        DrawEditorRoll();
        DrawEditorTools();
        DrawEditorTracks();
        ImGui.EndDisabled();
        DrawEditorDevices();
    }

    private bool EditorTransportButton(string label) => editorLargeTransport
        ? ImGui.Button(label, new Vector2(0, 38)) : ImGui.Button(label);

    private void DrawEditorTransport()
    {
        ImGui.BeginDisabled(EditorBusy);
        if (EditorTransportButton("Play from cursor##editor")) StartEditorPlayback();
        ImGui.SameLine(); ImGui.BeginDisabled(!EditorPlaying && !editorExternalPaused || editorPreview && plugin.AudioPreview.IsLoading);
        bool paused = editorExternalPaused || editorPreview && plugin.AudioPreview.IsPaused;
        if (EditorTransportButton(paused ? "Resume##editor" : "Pause##editor"))
        {
            if (editorDevices.Playing)
            {
                SetEditorCursor(EditorPlaybackCursor(), true);
                EditorDeviceAction(() => { editorDevices.StopPlayback(); editorExternalPaused = true; });
            }
            else if (editorExternalPaused) StartEditorPlayback();
            else if (editorPreview) plugin.AudioPreview.PauseResume();
        }
        ImGui.EndDisabled(); ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(editorJob is not null);
        if (EditorTransportButton("Stop##editor"))
        {
            EditorDeviceAction(() =>
            {
                if (editorDevices.Recording) FinishEditorRecording();
                else StopEditorPlayback();
            });
        }
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.BeginDisabled(EditorBusy);
        if (EditorTransportButton("Record##editor")) StartEditorRecording();
        ImGui.EndDisabled();
        if (editorDevices.Recording) ImGui.TextUnformatted($"Recording · {editorDevices.TakeMessages:N0} MIDI messages");
        ImGui.BeginDisabled(EditorBusy || EditorPlaying);
        if (ImGui.SmallButton("Beginning##editor")) SetEditorCursor(0, true);
        ImGui.SameLine(); if (ImGui.SmallButton("Previous bar##editor")) SetEditorCursor(editor!.Clock.Bar(Math.Max(0, editorCursor - 1)).Start, true);
        ImGui.SameLine(); if (ImGui.SmallButton("Next bar##editor")) SetEditorCursor(editor!.Clock.Bar(editorCursor).End, true);
        if (ImGui.SmallButton("Previous marker##editor"))
            SetEditorCursor(editor!.State.Events.LastOrDefault(e => e.Kind == EditorEventKind.Marker && e.Tick < editorCursor)?.Tick ?? 0, true);
        ImGui.SameLine(); if (ImGui.SmallButton("Next marker##editor"))
            SetEditorCursor(editor!.State.Events.FirstOrDefault(e => e.Kind == EditorEventKind.Marker && e.Tick > editorCursor)?.Tick ?? editor.State.EndTick, true);
        ImGui.SetNextItemWidth(150); ImGui.SliderFloat("Playback speed##editor", ref editorSpeed, .5f, 2, "%.2fx");
        ImGui.Checkbox("Metronome##editor", ref editorClick);
        ImGui.SameLine(); ImGui.Checkbox("Overdub with playback##editor", ref editorOverdub);
        ImGui.EndDisabled();
        ImGui.Checkbox("Follow playhead##editor", ref editorFollow);
        ImGui.SameLine(); ImGui.Checkbox("Large controls##editor", ref editorLargeTransport);
        if (editorTake is not null)
        {
            ImGui.Separator(); ImGui.TextUnformatted("Review MIDI take");
            ImGui.Checkbox("Keep notes##editor", ref editorRecordNotes);
            ImGui.SameLine(); ImGui.Checkbox("Keep controller and other channel events##editor", ref editorRecordControls);
            if (ImGui.BeginCombo("Destination track##editorTake", editor!.State.Tracks.First(t => t.Id == editorTrack).Name))
            {
                foreach (var track in editor.State.Tracks)
                    if (ImGui.Selectable(track.Name + "##TakeTrack" + track.Id, track.Id == editorTrack)) SetEditorTrack(track);
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(120); ImGui.SliderInt("Destination channel (0 = original)##editorTake", ref editorRecordChannel, 0, 16);
            if (ImGui.Button("Add take to active track##editor"))
            {
                var take = editorTake.Where(e => e.Kind == EditorEventKind.Note ? editorRecordNotes : editorRecordControls)
                    .Select(e => e with { Channel = editorRecordChannel == 0 ? e.Channel : editorRecordChannel, Track = editorTrack }).ToArray();
                ApplyEditor(() => { editor!.Add(take); editorTake = null; });
            }
            ImGui.SameLine(); if (ImGui.Button("Discard take##editor")) { editorTake = null; editorStatus = "MIDI take discarded."; }
        }
    }

    private void SetEditorCursor(long tick, bool show)
    {
        editorCursor = Math.Clamp(tick, 0, MidiEditorDocument.MaxTick);
        if (show && (editorCursor < editorViewStart || editorCursor >= editorViewStart + EditorSpan))
            editorViewStart = Math.Max(0, editorCursor - EditorSpan / 8);
    }

    private void DrawEditorDevices()
    {
        if (!ImGui.CollapsingHeader("MIDI devices and SoundFont##editor")) return;
        ImGui.BeginDisabled(EditorBusy || EditorPlaying);
        if (ImGui.Button("Refresh MIDI ports##editor"))
        {
            try { editorInputs = MidiEditorDevices.Inputs(); editorOutputs = MidiEditorDevices.Outputs(); }
            catch (Exception ex) { editorStatus = ex.Message; }
        }
        string inputLabel = editorDevices.InputConnected ? editorDevices.InputName : "Choose MIDI input";
        if (ImGui.BeginCombo("MIDI input##editor", inputLabel))
        {
            foreach (var port in editorInputs)
                if (ImGui.Selectable(port.Name + "##editorIn" + port.Index))
                {
                    EditorDeviceAction(() => { editorDevices.OpenInput(port); cfg.MidiEditorInputDevice = port.Name; cfg.Save(); });
                }
            ImGui.EndCombo();
        }
        if (editorDevices.InputConnected && ImGui.SmallButton("Disconnect input##editor"))
            EditorDeviceAction(() => { editorDevices.CloseInput(); cfg.MidiEditorInputDevice = ""; cfg.Save(); });
        if (ImGui.BeginCombo("MIDI output##editor", editorDevices.OutputConnected ? editorDevices.OutputName : "Choose MIDI output"))
        {
            for (int i = 0; i < editorOutputs.Length; i++)
                if (ImGui.Selectable(editorOutputs[i] + "##editorOut" + i))
                {
                    EditorDeviceAction(() => { editorDevices.OpenOutput(i); cfg.MidiEditorOutputDevice = editorDevices.OutputName; cfg.Save(); });
                }
            ImGui.EndCombo();
        }
        if (editorDevices.OutputConnected && ImGui.SmallButton("Disconnect output##editor"))
            EditorDeviceAction(() => { editorDevices.CloseOutput(); cfg.MidiEditorOutputDevice = ""; cfg.Save(); });
        bool remember = cfg.MidiEditorRememberDevices;
        if (ImGui.Checkbox("Reconnect devices when opening the editor##editor", ref remember))
        { cfg.MidiEditorRememberDevices = remember; cfg.Save(); }
        ImGui.Checkbox("Play to MIDI output##editor", ref editorExternal);
        bool manual = editorDevices.ExplicitNoteOffs;
        if (ImGui.Checkbox("Send explicit note-offs on stop##editor", ref manual))
        { editorDevices.ExplicitNoteOffs = cfg.MidiEditorExplicitNoteOffs = manual; cfg.Save(); }
        bool thru = editorDevices.Thru;
        if (ImGui.Checkbox("Connect input to output (MIDI thru)##editor", ref thru))
        {
            try { if (!EditorGameBusy) editorDevices.SetThru(thru); } catch (Exception ex) { editorStatus = ex.Message; }
        }
        string font = string.IsNullOrWhiteSpace(editorFont) ? cfg.PreviewSoundFontPath ?? "" : editorFont;
        ImGui.TextWrapped(font.Length > 0 ? Path.GetFileName(font) : "Choose a SoundFont for local audio.");
        if (ImGui.Button("Choose SoundFont…##editor")) fileDialogs.OpenFileDialog("Choose MIDI Editor SoundFont", ".sf2",
            (ok, paths) => { if (ok && paths.Count > 0) editorFont = paths[0]; }, 1, GetFileDialogStartFolder());
        ImGui.EndDisabled();
        if (ImGui.SmallButton("Panic: release MIDI output notes##editor"))
        {
            EditorDeviceAction(() =>
            {
                if (editorDevices.Recording) FinishEditorRecording();
                StopEditorPlayback(); editorDevices.Panic();
            });
        }
    }
}
