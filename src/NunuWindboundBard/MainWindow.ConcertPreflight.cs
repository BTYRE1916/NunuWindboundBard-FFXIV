using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private sealed record SetSongCheck(string Name, string Hash, string[] Issues);
    private Task<SetSongCheck[]>? concertPreflightWork;
    private SetSongCheck[]? concertPreflightResults;
    private string[] concertPreflightPaths = [];
    private string concertPreflightStatus = "";
    private Guid concertScanRequest, concertScanComplete, concertScanInProgress;
    private string[] concertInventory = [];
    private Task<string[]>? concertInventoryWork;

    private void ResetConcertInventory()
    {
        concertScanRequest = concertScanComplete = concertScanInProgress = Guid.Empty;
        concertInventory = [];
        concertInventoryWork = null;
    }

    private void StartConcertPreflight()
    {
        try
        {
            var snapshot = CaptureConcertSnapshot("Concert preflight");
            if (snapshot.PlaylistFiles.Count == 0) throw new InvalidOperationException("Add songs to the Ensemble playlist first.");
            var roster = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party).ToDictionary(m => m.ContentId, m => m.Name);
            concertPreflightPaths = cfg.Playlist.ToArray();
            concertPreflightStatus = "Checking every MIDI, track assignment and instrument plan.";
            concertPreflightResults = null;
            concertPreflightWork = Task.Run(() => CheckConcertSet(snapshot, roster));
            concertScanRequest = concertLink is not null ? Guid.NewGuid() : Guid.Empty;
        }
        catch (Exception ex) { concertPreflightStatus = "Preflight failed: " + ex.Message; }
    }

    private static SetSongCheck[] CheckConcertSet(ConcertSnapshot snapshot, Dictionary<ulong, string> roster)
    {
        return snapshot.PlaylistFiles.Select(path =>
        {
            var issues = new List<string>();
            string name = Path.GetFileName(path);
            string hash = "";
            try
            {
                var midi = MidiFileReader.Load(path);
                hash = GamePartyCueProtocol.GetHash(path);
                var keys = MidiOptimization.Suggest(midi).Select(l => l.Key).ToArray();
                snapshot.Songs.TryGetValue(path, out var stored);
                Dictionary<MidiLaneKey, ulong> map;
                if (stored?.PartyTracks.Count > 0)
                    map = keys.Where(key => stored.PartyTracks.ContainsKey(PartKey(key)))
                        .ToDictionary(key => key, key => stored.PartyTracks[PartKey(key)]);
                else
                    map = snapshot.Lineup?.Apply(keys).Assignments ?? new();

                int unassigned = keys.Count(key => !map.TryGetValue(key, out ulong id) || id == 0);
                if (unassigned > 0) issues.Add($"{unassigned} track/channel part(s) unassigned. Review any intentionally silent lanes.");
                var byBard = map.Where(pair => pair.Value != 0).GroupBy(pair => pair.Value);
                foreach (var group in byBard)
                {
                    ulong member = group.Key;
                    if (!roster.TryGetValue(member, out string? bard))
                    {
                        issues.Add("An assigned bard is absent from the current party.");
                        continue;
                    }
                    string key = member.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
                    if (stored?.PartyInstruments.GetValueOrDefault(key) is > 0) continue;
                    var owned = group.Select(pair => pair.Key).ToHashSet();
                    int melodic = midi.LaneInstruments.Count(lane => !lane.IsPercussion);
                    var instruments = midi.LaneInstruments
                        .Where(lane => owned.Contains(new MidiLaneKey(lane.Track, lane.Channel)))
                        .Select(lane => MidiInstrumentInference.ForLane(lane, midi.FileName, melodic).InstrumentId)
                        .Where(id => id != 0).Distinct().ToArray();
                    if (instruments.Length > 1) issues.Add($"{bard} has parts tagged for multiple instruments. Set an explicit instrument or split the parts.");
                }
            }
            catch (Exception ex) { issues.Add(ex.Message); }
            return new SetSongCheck(name, hash, issues.Distinct().ToArray());
        }).ToArray();
    }

    private void PollConcertWork()
    {
        if (concertPreflightWork is { IsCompleted: true } check)
        {
            concertPreflightWork = null;
            try
            {
                concertPreflightResults = check.GetAwaiter().GetResult();
                concertPreflightStatus = $"Checked {concertPreflightResults.Length} song(s).";
            }
            catch (Exception ex) { concertPreflightStatus = "Preflight failed: " + ex.Message; }
        }

        if (concertInventoryWork is { IsCompleted: true } inventory)
        {
            concertInventoryWork = null;
            try
            {
                concertInventory = inventory.GetAwaiter().GetResult();
                concertScanComplete = concertScanInProgress;
            }
            catch (Exception ex) { liveConcertStatus = "Could not inventory local MIDIs: " + ex.Message; }
        }
        if (concertLink is { } link && link.Me != link.Leader && concertInventoryWork is null &&
            TryFreshConcertPeer(link.Leader, Stopwatch.GetTimestamp(), out var leader) &&
            leader.Version == ConcertBuild && leader.ScanRequested != Guid.Empty &&
            leader.ScanRequested != concertScanInProgress)
        {
            concertScanInProgress = leader.ScanRequested;
            string[] paths = BuildNativeSongCandidates(-1).Concat(localSongSyncCuePaths.Values)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(ConcertLocalSession.MaxInventory).ToArray();
            concertInventoryWork = Task.Run(() => FingerprintConcertFiles(paths));
        }

        PollConcertPack();
    }

    private static string[] FingerprintConcertFiles(string[] paths)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path) && IsMidiPath(path) && new FileInfo(path).Length is >= 14 and <= 50 * 1024 * 1024)
                    hashes.Add(GamePartyCueProtocol.GetHash(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return hashes.ToArray();
    }

    private void DrawConcertPreflight()
    {
        if (!ImGui.TreeNode(Loc.T("Whole-set preflight"))) return;
        ImGui.BeginDisabled(concertPreflightWork is not null);
        if (ImGui.Button(Loc.T("Check setlist"))) StartConcertPreflight();
        ImGui.EndDisabled();
        if (concertPreflightStatus.Length > 0) ImGui.TextWrapped(concertPreflightStatus);
        if (concertPreflightResults is { } results)
        {
            if (!concertPreflightPaths.SequenceEqual(cfg.Playlist, StringComparer.OrdinalIgnoreCase))
                ImGui.TextWrapped("The playlist changed. Run the check again.");
            else
            {
                string[] checkedHashes = results.Where(row => row.Hash.Length > 0)
                    .Select(row => row.Hash).Distinct().ToArray();
                long now = Stopwatch.GetTimestamp();
                var roster = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party).Where(m => !m.IsLocal).ToArray();
                var confirmed = new Dictionary<ulong, HashSet<string>>();
                foreach (var member in roster)
                {
                    string status;
                    if (!TryFreshConcertPeer(member.ContentId, now, out var peer)) status = "local inventory unavailable";
                    else if (peer.Version != ConcertBuild) status = $"plugin version {peer.Version}, expected {ConcertBuild}";
                    else if (concertScanRequest == Guid.Empty || peer.ScanFinished != concertScanRequest)
                        status = "waiting for MIDI inventory";
                    else
                    {
                        var hashes = peer.Files.ToHashSet(StringComparer.Ordinal);
                        confirmed[member.ContentId] = hashes;
                        status = $"has {checkedHashes.Count(hashes.Contains)}/{checkedHashes.Length} exact MIDIs";
                    }
                    ImGui.TextUnformatted($"{member.Name}: {status}");
                }
                if (confirmed.Count < roster.Length)
                    ImGui.TextWrapped("Enable same-PC concert recovery on every local bard, then check again to verify their files.");
                ImGui.BeginChild("##ConcertWholeSet", new System.Numerics.Vector2(-1, 165), true);
                foreach (var row in results)
                {
                    var missing = row.Hash.Length == 0 ? [] : roster
                        .Where(member => confirmed.TryGetValue(member.ContentId, out var have) && !have.Contains(row.Hash))
                        .Select(member => member.Name).ToArray();
                    ImGui.TextUnformatted(row.Name);
                    foreach (string issue in row.Issues) ImGui.BulletText(issue);
                    if (missing.Length > 0) ImGui.BulletText("Missing exact MIDI: " + string.Join(", ", missing));
                }
                ImGui.EndChild();
            }
        }
        ImGui.TreePop();
    }

    private ulong rehearsalBard;
    private float rehearsalFrom;
    private float rehearsalTo = 10;
    private bool rehearsalLoop = true;
    private string rehearsalStatus = "";

    private void DrawConcertRehearsal()
    {
        if (!ImGui.TreeNode(Loc.T("Rehearse assigned parts"))) return;
        if (song is null || partyAssignments.Count == 0)
            ImGui.TextWrapped("Load a song and assign its ensemble parts first.");
        else
        {
            if (ImGui.BeginCombo("Audition", rehearsalBard == 0 ? "All assigned bards" : PartyMemberName(rehearsalBard)))
            {
                if (ImGui.Selectable("All assigned bards", rehearsalBard == 0)) rehearsalBard = 0;
                foreach (ulong id in partyAssignments.Values.Where(v => v != 0).Distinct())
                    if (ImGui.Selectable($"{PartyMemberName(id)}##rehearse{id}", rehearsalBard == id))
                        rehearsalBard = id;
                ImGui.EndCombo();
            }
            ImGui.Checkbox("Loop section", ref rehearsalLoop);
            if (rehearsalLoop)
            {
                ImGui.InputFloat("From (seconds)", ref rehearsalFrom, 1f);
                ImGui.InputFloat("To (seconds)", ref rehearsalTo, 1f);
            }
            if (ImGui.Button("Start local SoundFont rehearsal")) StartConcertRehearsal();
            ImGui.SameLine();
            if (ImGui.Button("Stop rehearsal")) plugin.AudioPreview.Stop();
            if (rehearsalStatus.Length > 0) ImGui.TextWrapped(rehearsalStatus);
            ImGui.TextUnformatted(plugin.AudioPreview.Status);
        }
        ImGui.TreePop();
    }

    private void StartConcertRehearsal()
    {
        try
        {
            if (song is not { } current || plugin.Player.IsPlaying || ConcertTakePlaying(liveTake, Stopwatch.GetTimestamp()))
                throw new InvalidOperationException("Stop the live take before rehearsing.");
            double startMs = rehearsalLoop ? rehearsalFrom * 1000d : 0d;
            double endMs = rehearsalLoop ? rehearsalTo * 1000d : current.DurationMs;
            if (!double.IsFinite(startMs) || !double.IsFinite(endMs) || startMs < 0 || endMs <= startMs ||
                endMs > current.DurationMs + 1)
                throw new InvalidOperationException($"Choose a section within 0–{current.DurationMs / 1000:0.00} seconds.");

            var mix = new List<BardNote>();
            int input = 0, shifted = 0;
            foreach (var group in partyAssignments.Where(pair => pair.Value != 0 &&
                         (rehearsalBard == 0 || pair.Value == rehearsalBard)).GroupBy(pair => pair.Value))
            {
                var keys = group.Select(pair => pair.Key).ToHashSet();
                var plans = lanes.Select(lane => lane with { Include = keys.Contains(lane.Key) }).ToArray();
                MidiSong excerpt = current with { Notes = current.Notes
                    .Where(note => note.TimeMs >= startMs && note.TimeMs < endMs && keys.Contains(new(note.Track, note.Channel)))
                    .ToArray() };
                var options = new MidiOptimizeOptions(plans, cfg.RepairOctaves);
                var report = MidiOptimization.Analyze(excerpt, options);
                shifted += report.ShiftedNotes + report.RepairedNotes;
                input += excerpt.Notes.Count;
                var prepared = BardArranger.Build(current, new BardArrangeOptions(options, (ChordStrategy)cfg.ChordStrategy,
                    cfg.GlobalTranspose, cfg.MinimumGapMs, cfg.ArpeggioSpacingMs));
                foreach (var note in prepared.Notes.Where(n => n.TimeMs >= startMs && n.TimeMs < endMs))
                    mix.Add(note with { TimeMs = note.TimeMs - startMs,
                        DurationMs = Math.Min(endMs - note.TimeMs, Math.Clamp(note.DurationMs, cfg.KeyHoldMs, 2000d)) });
            }
            var preview = new BardArrangement(mix, new BardArrangeReport(input, 0, 0, 0, 0, mix.Count, endMs - startMs));
            byte[] bytes = AudioPreviewMidiBuilder.Build(current, preview, AudioPreviewMode.WindboundArrangement, null,
                minimumDurationMs: endMs - startMs);
            plugin.AudioPreview.SetVolume(cfg.PreviewVolume);
            plugin.AudioPreview.SetSpeed(cfg.PlaybackSpeed);
            plugin.AudioPreview.Start(loaded, cfg.PreviewSoundFontPath, bytes, loop: rehearsalLoop);
            rehearsalStatus = $"Conductor's arrangement: {mix.Count} scheduled note(s), {Math.Max(0, input - mix.Count)} omitted and {shifted} octave changes. Transpose: {cfg.GlobalTranspose:+#;-#;0}.";
        }
        catch (Exception ex) { rehearsalStatus = ex.Message; }
    }
}
