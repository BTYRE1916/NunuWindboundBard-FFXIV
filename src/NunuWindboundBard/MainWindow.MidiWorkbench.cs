using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private MidiWorkbenchDocument? workbench;
    private readonly HashSet<Guid> workbenchSelection = new();
    private Guid workbenchActive;
    private string workbenchStatus = "Import a MIDI or copy the song loaded in the player.";
    private string workbenchRename = "";
    private string workbenchExportName = "Windbound Workbench.mid";
    private int workbenchTranspose, workbenchVoiceLimit = 1, workbenchQuantize = 2;
    private int workbenchSelectedNote = -1, workbenchPitch = 60, workbenchVelocity = 90;
    private float workbenchNoteTime, workbenchNoteDuration = 150, workbenchExtend = 1000;
    private float workbenchRollStart, workbenchRollLength = 15;
    private int workbenchStatsRevision = -1;
    private MidiWorkbenchDocument? workbenchStatsDocument;
    private readonly Dictionary<Guid, (int Low, int High)> workbenchRanges = new();
    private double workbenchDuration;
    private int workbenchNoteCount;
    private Task<WorkbenchJob>? workbenchTask;
    private sealed record WorkbenchJob(string Message, MidiWorkbenchDocument? Document = null,
        string Path = "", byte[]? Preview = null, string SoundFont = "", bool AddAndLoad = false);

    private void RunWorkbench(Func<WorkbenchJob> job)
    {
        if (workbenchTask is not null) return;
        workbenchTask = Task.Run(job);
        // Observe failures even if the plugin closes before the UI polls again.
        _ = workbenchTask.ContinueWith(t => { _ = t.Exception; },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        workbenchStatus = "Working…";
    }

    private void PollWorkbench()
    {
        if (workbenchTask is not {IsCompleted: true}) return;
        var task = workbenchTask; workbenchTask = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (result.Document is not null)
            {
                workbench = result.Document; workbenchSelection.Clear();
                SetWorkbenchActive(workbench.Tracks.FirstOrDefault());
                workbenchRollStart = 0;
                workbenchExportName = System.IO.Path.GetFileNameWithoutExtension(workbench.SourcePath) + " - Workbench.mid";
            }
            if (result.Preview is not null)
            {
                if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                    throw new InvalidOperationException("Stop game playback before starting the local preview.");
                plugin.AudioPreview.SetVolume(cfg.PreviewVolume); plugin.AudioPreview.SetSpeed(cfg.PreviewSpeed);
                plugin.AudioPreview.Start(workbench!.SourcePath, result.SoundFont, result.Preview);
            }
            if (result.AddAndLoad)
            {
                var snapshot = workbench!.Snapshot();
                int parts = snapshot.Tracks.Count(t => t.Enabled && t.Notes.Count > 0);
                SavedPlaylistLibrary.AddMidiToPurpose(cfg, result.Path, parts == 1 ? 0 : 1);
                var solo = snapshot.Tracks.FirstOrDefault(t => t.Enabled && t.Notes.Count > 0);
                if (parts == 1 && solo is {InstrumentId: > 0}) cfg.InstrumentsByFile[result.Path] = solo.InstrumentId;
                cfg.Save();
                if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                {
                    workbenchStatus = "MIDI saved and added to the library: " + result.Path + ". Stop playback before loading this arrangement.";
                    return;
                }
                LoadFromUser(result.Path);
            }
            workbenchStatus = result.Message;
        }
        catch (Exception ex) { workbenchStatus = "Workbench: " + ex.Message; }
    }

    private void ImportWorkbench(string path)
    {
        plugin.AudioPreview.Stop("Importing a MIDI into the workbench.");
        RunWorkbench(() => new("MIDI imported into an independent working copy.", MidiWorkbenchDocument.Load(path)));
    }

    private void SetWorkbenchActive(WorkbenchTrack? track)
    {
        workbenchActive = track?.Id ?? Guid.Empty; workbenchRename = track?.Name ?? "";
        workbenchSelectedNote = -1;
    }
    private HashSet<Guid> WorkbenchScope() => workbenchSelection.Count > 0
        ? workbenchSelection.ToHashSet() : workbenchActive == Guid.Empty ? new() : new() {workbenchActive};

    private void EditWorkbench(Action edit)
    {
        try
        {
            edit();
            plugin.AudioPreview.Stop("Workbench changed; preview the edited MIDI again.");
            workbenchSelection.RemoveWhere(id => !workbench!.Tracks.Any(t => t.Id == id));
            var active = workbench!.Tracks.FirstOrDefault(t => t.Id == workbenchActive) ?? workbench.Tracks.FirstOrDefault();
            SetWorkbenchActive(active);
            workbenchStatus = $"Working copy updated. {workbench.Tracks.Count} parts / {workbench.Tracks.Sum(t => t.Notes.Count):N0} notes. Undo is available.";
        }
        catch (Exception ex) { workbenchStatus = "Edit: " + ex.Message; }
    }

    private void DrawMidiWorkbench()
    {
        PollWorkbench();
        cfg.MidiWorkbench ??= new(); cfg.MidiWorkbench.DrumMap ??= MidiWorkbenchSettings.DefaultDrums();
        ImGui.TextColored(new Vector4(.45f, .86f, 1, 1), Loc.T("MIDI WORKBENCH"));
        ImGui.TextWrapped(Loc.T("Prepare MIDI tracks for your band: edit parts, split drums, repair ranges and export a new arrangement. Source files stay unchanged. Local audio uses your selected SoundFont."));
        ImGui.BeginDisabled(workbenchTask is not null);
        if (ImGui.Button(Loc.T("Import MIDI…##workbench")))
            fileDialogs.OpenFileDialog(Loc.T("Import MIDI into Workbench"), MidiFilter,
                (bool ok, List<string> files) => { if (ok && files.Count > 0) ImportWorkbench(files[0]); }, 1, GetFileDialogStartFolder());
        ImGui.SameLine(); ImGui.BeginDisabled(song is null || string.IsNullOrEmpty(loaded));
        if (ImGui.Button(Loc.T("Copy current player MIDI"))) ImportWorkbench(loaded);
        ImGui.EndDisabled();
        ImGui.TextWrapped(Loc.T(workbenchStatus));
        if (workbench is null) { ImGui.EndDisabled(); return; }
        if (workbenchStatsDocument != workbench || workbenchStatsRevision != workbench.Revision)
        {
            workbenchRanges.Clear(); workbenchDuration = 0; workbenchNoteCount = 0;
            foreach (var t in workbench.Tracks)
            {
                int low = 127, high = 0;
                foreach (var n in t.Notes) { low = Math.Min(low, n.Pitch); high = Math.Max(high, n.Pitch); workbenchDuration = Math.Max(workbenchDuration, n.TimeMs + n.DurationMs); }
                workbenchRanges[t.Id] = t.Notes.Count == 0 ? (48, 84) : (low, high);
                workbenchNoteCount += t.Notes.Count;
            }
            workbenchStatsDocument = workbench; workbenchStatsRevision = workbench.Revision;
        }
        ImGui.TextDisabled(System.IO.Path.GetFileName(workbench.SourcePath) +
            $"  |  {workbenchDuration / 1000:0.#}s  |  {workbenchNoteCount:N0} notes");
        ImGui.BeginDisabled(!workbench.CanUndo);
        if (ImGui.SmallButton(Loc.T("Undo##workbench"))) EditWorkbench(workbench.Undo);
        ImGui.EndDisabled(); ImGui.SameLine(); ImGui.BeginDisabled(!workbench.CanRedo);
        if (ImGui.SmallButton(Loc.T("Redo##workbench"))) EditWorkbench(workbench.Redo);
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Select all##workbench"))) { workbenchSelection.Clear(); foreach (var t in workbench.Tracks) workbenchSelection.Add(t.Id); }
        ImGui.SameLine(); if (ImGui.SmallButton(Loc.T("Clear selection##workbench"))) workbenchSelection.Clear();
        DrawWorkbenchTracks();
        var active = workbench.Tracks.FirstOrDefault(t => t.Id == workbenchActive);
        if (active is not null)
        {
            ImGui.SetNextItemWidth(Math.Max(160, ImGui.GetContentRegionAvail().X - 125));
            ImGui.InputText(Loc.T("##workbenchRename"), ref workbenchRename, 240);
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Rename part"))) EditWorkbench(() => workbench.SetTrack(active with {Name = workbenchRename.Trim()}));
            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo(Loc.T("Part instrument##workbench"), active.InstrumentId == 0 ? Loc.T("Source / choose later") : Loc.T(InstrumentCatalog.Display(active.InstrumentId))))
            {
                if (ImGui.Selectable(Loc.T("Source / choose later"), active.InstrumentId == 0))
                    EditWorkbench(() => workbench.SetTrack(active with {InstrumentId = 0}));
                foreach (var instrument in InstrumentCatalog.All)
                    if (InstrumentIconSelectable(instrument.Id, instrument.DisplayName, "WorkbenchInstrument" + instrument.Id, active.InstrumentId == instrument.Id))
                        EditWorkbench(() => workbench.SetTrack(active with {InstrumentId = instrument.Id, IsDrumKit = false}));
                ImGui.EndCombo();
            }
            bool kit = active.IsDrumKit;
            if (ImGui.Checkbox(Loc.T("Treat active part as a General MIDI drum kit"), ref kit))
                EditWorkbench(() => workbench.SetTrack(active with {IsDrumKit = kit, InstrumentId = kit ? 0 : active.InstrumentId}));
            ImGui.TextDisabled(Loc.T("Leave this off for already prepared Bassdrum / SnareDrum parts."));
            int octave = active.OctaveShift;
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt(Loc.T("Octave tag (semitones)##workbench"), ref octave, 12, 12))
                EditWorkbench(() => workbench.SetTrack(active with {OctaveShift = Math.Clamp((int)Math.Round(octave / 12.0) * 12, -48, 48)}));
            ImGui.TextDisabled(Loc.T("The piano roll shows stored MIDI pitches. Preview applies the octave tag; export preserves it."));
            DrawWorkbenchRoll(active);
        }
        if (ImGui.CollapsingHeader(Loc.T("Track and note tools##workbench"), ImGuiTreeNodeFlags.DefaultOpen)) DrawWorkbenchTools();
        if (ImGui.CollapsingHeader(Loc.T("Drum split and custom pitch map##workbench"))) DrawWorkbenchDrums();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.65f, 1, .76f, 1), Loc.T("PREVIEW & EXPORT"));
        ImGui.BeginDisabled(plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending);
        if (ImGui.Button(Loc.T("Preview enabled parts##workbench"))) PreviewWorkbench(false);
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Solo selected##workbench"))) PreviewWorkbench(true);
        ImGui.EndDisabled();
        if (ImGui.SmallButton(Loc.T("Stop audio##workbench"))) plugin.AudioPreview.Stop();
        ImGui.SameLine(); if (ImGui.SmallButton(Loc.T("Choose SoundFont##workbench"))) BrowseSoundFont();
        ImGui.TextDisabled(Loc.T("Preview uses General MIDI approximations of FFXIV instruments. It sends no game notes."));
        ImGui.SetNextItemWidth(330); ImGui.InputText(Loc.T("Export filename##workbench"), ref workbenchExportName, 200);
        if (ImGui.Button(Loc.T("Export MIDI as…##workbench"))) SaveWorkbenchDialog(false);
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Export, add to library & load"))) SaveWorkbenchToLibrary();
        if (ImGui.SmallButton(Loc.T("Export source lyrics (.lrc)"))) SaveWorkbenchDialog(true);
        ImGui.TextDisabled(Loc.T("Export keeps the source tempo map, meter, key and lyric events. Existing files are preserved."));
        ImGui.EndDisabled();
    }

    private void DrawWorkbenchTracks()
    {
        if (!ImGui.BeginTable("##workbenchTracks", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, 190))) return;
        ImGui.TableSetupColumn(Loc.T("Select"), ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn(Loc.T("Use"), ImGuiTableColumnFlags.WidthFixed, 32);
        ImGui.TableSetupColumn(Loc.T("Part")); ImGui.TableSetupColumn(Loc.T("Instrument"));
        ImGui.TableSetupColumn(Loc.T("Notes")); ImGui.TableSetupColumn(Loc.T("Range")); ImGui.TableHeadersRow();
        foreach (var t in workbench!.Tracks.ToArray())
        {
            ImGui.PushID(t.Id.ToString()); ImGui.TableNextRow(); ImGui.TableNextColumn();
            bool selected = workbenchSelection.Contains(t.Id);
            if (ImGui.Checkbox("##selected", ref selected))
            { if (selected) workbenchSelection.Add(t.Id); else workbenchSelection.Remove(t.Id); }
            ImGui.TableNextColumn(); bool enabled = t.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled)) EditWorkbench(() => workbench.SetTrack(t with {Enabled = enabled}));
            ImGui.TableNextColumn();
            if (ImGui.Selectable(t.Name + "##part", workbenchActive == t.Id)) SetWorkbenchActive(t);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(t.IsDrumKit ? Loc.T("GM drum kit") : Loc.T(InstrumentCatalog.Display(t.InstrumentId)));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(t.Notes.Count.ToString("N0"));
            ImGui.TableNextColumn();
            if (t.Notes.Count > 0)
            {
                var range = workbenchRanges.GetValueOrDefault(t.Id, (48, 84));
                ImGui.TextUnformatted(NoteMapper.GetNoteName(range.Item1) + "–" + NoteMapper.GetNoteName(range.Item2));
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawWorkbenchTools()
    {
        var ids = WorkbenchScope();
        ImGui.TextDisabled(Loc.T($"Applies to {ids.Count} selected part(s); the active part is used when none are checked."));
        ImGui.BeginDisabled(ids.Count == 0);
        if (ImGui.Button(Loc.T("Duplicate selected"))) EditWorkbench(() => workbench!.Duplicate(ids));
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Remove selected parts"))) EditWorkbench(() => workbench!.Remove(ids));
        if (ImGui.Button(Loc.T("Merge using active instrument"))) EditWorkbench(() => workbench!.Merge(ids, workbenchActive));
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Split into single-note voices"))) EditWorkbench(() => workbench!.SplitVoices(ids));
        if (ImGui.SmallButton(Loc.T("Move active up"))) EditWorkbench(() => workbench!.Move(workbenchActive, -1));
        ImGui.SameLine(); if (ImGui.SmallButton(Loc.T("Move active down"))) EditWorkbench(() => workbench!.Move(workbenchActive, 1));
        ImGui.SetNextItemWidth(120); ImGui.InputInt(Loc.T("Semitones"), ref workbenchTranspose);
        workbenchTranspose = Math.Clamp(workbenchTranspose, -48, 48);
        if (ImGui.Button(Loc.T("Transpose selected"))) EditWorkbench(() => workbench!.Transform(ids, n => MidiWorkbenchDocument.Transpose(n, workbenchTranspose, false)));
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Adapt pitched parts to C3–C6"))) EditWorkbench(() => workbench!.AdaptRange(ids));
        if (ImGui.SmallButton(Loc.T("Bake octave tags into notes"))) EditWorkbench(() => workbench!.BakeOctaves(ids));
        if (ImGui.Button(Loc.T("Remove duplicate notes"))) EditWorkbench(() => workbench!.Transform(ids, MidiWorkbenchDocument.RemoveDuplicates));
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Trim same-pitch overlaps"))) EditWorkbench(() => workbench!.Transform(ids, MidiWorkbenchDocument.TrimOverlaps));
        ImGui.SetNextItemWidth(160); ImGui.SliderFloat(Loc.T("Sustain cap (ms)"), ref workbenchExtend, 50, 4000, "%.0f");
        if (ImGui.Button(Loc.T("Extend notes to next onset"))) EditWorkbench(() => workbench!.Transform(ids, n => MidiWorkbenchDocument.ExtendNotes(n, workbenchExtend)));
        ImGui.SetNextItemWidth(180); string[] grids = ["Quarter notes", "Eighth notes", "Sixteenth notes", "Thirty-second notes"];
        ImGui.Combo(Loc.T("Beat grid"), ref workbenchQuantize, grids, grids.Length);
        if (ImGui.Button(Loc.T("Quantize starts and ends"))) EditWorkbench(() => workbench!.Transform(ids, n => workbench.Quantize(n, 1 << workbenchQuantize)));
        ImGui.SetNextItemWidth(160); ImGui.SliderInt(Loc.T("Voice cap"), ref workbenchVoiceLimit, 1, 16);
        if (ImGui.Button(Loc.T("Limit sustained polyphony"))) EditWorkbench(() => workbench!.Transform(ids, n => MidiWorkbenchDocument.LimitVoices(n, workbenchVoiceLimit)));
        ImGui.TextDisabled(Loc.T("Voice limiting drops notes while the cap is full. Undo restores them."));
        ImGui.EndDisabled();
    }

    private void DrawWorkbenchDrums()
    {
        var settings = cfg.MidiWorkbench;
        bool convert = settings.ConvertDrumPitches;
        if (ImGui.Checkbox(Loc.T("Convert GM hits to playable FFXIV pitches"), ref convert))
        { settings.ConvertDrumPitches = convert; cfg.Save(); }
        ImGui.TextWrapped(Loc.T("Splits selected GM kits into Bass Drum, Snare Drum, Cymbal, Bongo and other percussion. Unmapped hits stay in Other Percussion. Named FFXIV parts are preserved; use the GM drum-kit checkbox only when conversion is intended."));
        if (ImGui.Button(Loc.T("Split selected GM drums"))) EditWorkbench(() => workbench!.SplitDrums(WorkbenchScope(), settings));
        ImGui.SameLine(); if (ImGui.SmallButton(Loc.T("Reset Windbound drum map"))) { settings.DrumMap = MidiWorkbenchSettings.DefaultDrums(); cfg.Save(); }
        if (!ImGui.BeginTable("##workbenchDrumMap", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(0, 250))) return;
        ImGui.TableSetupColumn(Loc.T("GM note")); ImGui.TableSetupColumn(Loc.T("Keep"));
        ImGui.TableSetupColumn(Loc.T("Destination instrument")); ImGui.TableSetupColumn(Loc.T("Target pitch")); ImGui.TableHeadersRow();
        var pitches = Enumerable.Range(35, 47).Concat(workbench!.Tracks.Where(t => t.IsDrumKit).SelectMany(t => t.Notes).Select(n => n.Pitch)).Distinct().Order().ToArray();
        foreach (int pitch in pitches)
        {
            var rule = settings.DrumMap.GetValueOrDefault(pitch, new WorkbenchDrumRule(0, pitch));
            ImGui.PushID(pitch); ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{pitch} ({NoteMapper.GetNoteName(pitch)})"); ImGui.TableNextColumn();
            bool keep = rule.Enabled;
            if (ImGui.Checkbox("##keep", ref keep)) { rule = rule with {Enabled = keep}; settings.DrumMap[pitch] = rule; cfg.Save(); }
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##drumInstrument", rule.InstrumentId == 0 ? Loc.T("Other / GM") : Loc.T(InstrumentCatalog.Display(rule.InstrumentId))))
            {
                foreach (int id in new[] {0, 12, 13, 14, 11, 10})
                    if (ImGui.Selectable(id == 0 ? Loc.T("Other / GM") : Loc.T(InstrumentCatalog.Display(id)), id == rule.InstrumentId))
                    { rule = rule with {InstrumentId = id, Pitch = MidiWorkbenchDocument.Fold(rule.Pitch)}; settings.DrumMap[pitch] = rule; cfg.Save(); }
                ImGui.EndCombo();
            }
            ImGui.TableNextColumn(); int target = rule.Pitch; ImGui.SetNextItemWidth(-1);
            ImGui.BeginDisabled(rule.InstrumentId == 0 || !convert);
            if (ImGui.InputInt("##targetPitch", ref target)) { settings.DrumMap[pitch] = rule with {Pitch = Math.Clamp(target, 48, 84)}; cfg.Save(); }
            ImGui.EndDisabled(); ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawWorkbenchRoll(WorkbenchTrack track)
    {
        if (!ImGui.CollapsingHeader(Loc.T("Piano roll and note editor##workbench"), ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.SetNextItemWidth(220); ImGui.SliderFloat(Loc.T("View start (seconds)"), ref workbenchRollStart, 0, (float)Math.Max(1, workbenchDuration / 1000), "%.1f");
        ImGui.SetNextItemWidth(220); ImGui.SliderFloat(Loc.T("Visible seconds"), ref workbenchRollLength, 2, 60, "%.1f");
        var origin = ImGui.GetCursorScreenPos(); float width = Math.Max(200, ImGui.GetContentRegionAvail().X), height = 200;
        var draw = ImGui.GetWindowDrawList();
        var range = workbenchRanges.GetValueOrDefault(track.Id, (48, 84));
        int low = Math.Min(48, range.Item1), high = Math.Max(84, range.Item2);
        float row = height / (high - low + 1);
        ImGui.InvisibleButton("##workbenchRoll", new Vector2(width, height));
        draw.AddRectFilled(origin, origin + new Vector2(width, height), 0xFF211727);
        float Y(int pitch) => origin.Y + (high - pitch) * row;
        draw.AddRectFilled(new(origin.X, Y(84)), new(origin.X + width, Y(48) + row), 0x443D7750);
        for (int p = low; p <= high; p++)
        {
            draw.AddLine(new(origin.X, Y(p)), new(origin.X + width, Y(p)), p % 12 == 0 ? 0x774C6060u : 0x224C6060u);
            if (p % 12 == 0) draw.AddText(new(origin.X + 3, Y(p)), 0xFFB7C9BF, NoteMapper.GetNoteName(p));
        }
        double begin = workbenchRollStart * 1000, span = workbenchRollLength * 1000;
        bool hovered = ImGui.IsItemHovered(); var mouse = ImGui.GetIO().MousePos;
        int hit = -1, displayed = 0;
        for (int i = 0; i < track.Notes.Count; i++)
        {
            var n = track.Notes[i];
            if (n.TimeMs > begin + span || n.TimeMs + n.DurationMs < begin) continue;
            if (++displayed > 20_000) break;
            float x1 = origin.X + (float)Math.Clamp((n.TimeMs - begin) / span, 0, 1) * width;
            float x2 = Math.Min(origin.X + width, Math.Max(x1 + 3, origin.X + (float)Math.Clamp((n.TimeMs + n.DurationMs - begin) / span, 0, 1) * width));
            Vector2 a = new(x1, Y(n.Pitch)), b = new(x2, Y(n.Pitch) + Math.Max(2, row - 1));
            draw.AddRectFilled(a, b, i == workbenchSelectedNote ? 0xFF68DFFFu : 0xFFE9AD5Bu);
            if (hovered && mouse.X >= a.X && mouse.X <= b.X && mouse.Y >= a.Y && mouse.Y <= b.Y) hit = i;
        }
        if (hit >= 0)
        {
            var n = track.Notes[hit]; ImGui.SetTooltip($"{NoteMapper.GetNoteName(n.Pitch)} | {n.TimeMs:0}ms | {n.DurationMs:0}ms | velocity {n.Velocity}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            { workbenchSelectedNote = hit; workbenchPitch = n.Pitch; workbenchVelocity = n.Velocity; workbenchNoteTime = (float)n.TimeMs; workbenchNoteDuration = (float)n.DurationMs; }
        }
        ImGui.TextDisabled(Loc.T("Click a note to edit it. Green shading marks C3–C6. Zoom in for dense sections."));
        ImGui.SetNextItemWidth(100); ImGui.InputInt(Loc.T("Pitch##wbNote"), ref workbenchPitch);
        ImGui.SameLine(); ImGui.SetNextItemWidth(100); ImGui.InputInt(Loc.T("Velocity##wbNote"), ref workbenchVelocity);
        ImGui.SetNextItemWidth(150); ImGui.InputFloat(Loc.T("Start ms##wbNote"), ref workbenchNoteTime, 10, 100, "%.1f");
        ImGui.SameLine(); ImGui.SetNextItemWidth(150); ImGui.InputFloat(Loc.T("Duration ms##wbNote"), ref workbenchNoteDuration, 10, 100, "%.1f");
        ImGui.TextDisabled(Loc.T("Note duration: 35–60,000 ms, matching Windbound's MIDI reader."));
        ImGui.BeginDisabled(workbenchSelectedNote < 0 || workbenchSelectedNote >= track.Notes.Count);
        if (ImGui.Button(Loc.T("Update selected note")))
        {
            int index = workbenchSelectedNote;
            EditWorkbench(() => workbench!.SetTrack(track with {Notes = track.Notes.Select((n, i) => i == index ? n with {
                Pitch = workbenchPitch, Velocity = workbenchVelocity, TimeMs = workbenchNoteTime, DurationMs = workbenchNoteDuration} : n).ToArray()}));
        }
        ImGui.SameLine(); if (ImGui.Button(Loc.T("Delete selected note")))
        { int index = workbenchSelectedNote; EditWorkbench(() => workbench!.SetTrack(track with {Notes = track.Notes.Where((n, i) => i != index).ToArray()})); }
        ImGui.EndDisabled();
        if (ImGui.Button(Loc.T("Add note with these values"))) EditWorkbench(() => workbench!.SetTrack(track with {
            Notes = track.Notes.Append(new MidiNote(0, workbenchNoteTime, workbenchPitch, workbenchVelocity, 0, 0, workbenchNoteDuration)).ToArray()}));
    }

    private void PreviewWorkbench(bool selected)
    {
        string sf2 = soundFontInput.Trim().Trim('"');
        if (!File.Exists(sf2)) { workbenchStatus = "Choose a SoundFont (.sf2) before previewing."; return; }
        var snapshot = workbench!.Snapshot(); var ids = WorkbenchScope();
        if (selected) snapshot = snapshot with {Tracks = snapshot.Tracks.Select(t => t with {Enabled = ids.Contains(t.Id)}).ToArray()};
        cfg.PreviewSoundFontPath = sf2; cfg.Save();
        RunWorkbench(() => new("Local workbench audio preview started.", Preview: MidiWorkbenchExport.Preview(snapshot), SoundFont: sf2));
    }
    private void SaveWorkbenchDialog(bool lyrics)
    {
        var snapshot = workbench!.Snapshot();
        string name = lyrics ? System.IO.Path.GetFileNameWithoutExtension(workbenchExportName) + ".lrc" : workbenchExportName;
        fileDialogs.SaveFileDialog(Loc.T(lyrics ? "Export MIDI lyrics" : "Export Workbench MIDI"),
            lyrics ? "Lyrics{.lrc}" : MidiFilter, name, lyrics ? ".lrc" : ".mid", (bool ok, string path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                if (!System.IO.Path.HasExtension(path)) path += lyrics ? ".lrc" : ".mid";
                string destination = path;
                RunWorkbench(() =>
                {
                    if (!lyrics) return new("MIDI exported and verified: " + MidiWorkbenchExport.Save(snapshot, destination));
                    if (!System.IO.Path.GetExtension(destination).Equals(".lrc", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Choose a .lrc filename for lyrics.");
                    string text = MidiWorkbenchExport.Lyrics(snapshot);
                    using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(text);
                    return new("Lyrics exported: " + destination);
                });
            }, GetFileDialogStartFolder());
    }
    private void SaveWorkbenchToLibrary()
    {
        string folder = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "WorkbenchArrangements");
        string stem = System.IO.Path.GetFileNameWithoutExtension(workbenchExportName);
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) stem = stem.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(stem)) stem = "Windbound Workbench";
        string destination = System.IO.Path.Combine(folder, stem + "-" + Guid.NewGuid().ToString("N")[..8] + ".mid");
        var snapshot = workbench!.Snapshot();
        RunWorkbench(() => new("MIDI exported, added to the library and loaded in Windbound.",
            Path: MidiWorkbenchExport.Save(snapshot, destination), AddAndLoad: true));
    }
}
