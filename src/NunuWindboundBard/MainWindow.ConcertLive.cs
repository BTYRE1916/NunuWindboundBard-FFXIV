using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private sealed record ConcertTake(MidiSong Song, BardArrangement Arrangement, string Hash,
        string Parts, long Signal, long Origin, BardPlaybackSettings Settings, int Instrument);
    private sealed record ConcertLink(string Directory, ulong Me, ulong Leader, string Roster);
    private static readonly string ConcertBuild = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
    private ConcertTake? liveTake;
    private bool allowConcertLocal;
    private ConcertLink? concertLink;
    private Task<Dictionary<ulong, ConcertLocalPeer>>? liveExchange;
    private ConcertLink? exchangeLink;
    private Dictionary<ulong, ConcertLocalPeer> livePeers = new();
    private long nextConcertExchange;
    private ConcertRejoinCue? outgoingRejoin;
    private ConcertRejoinCue? pendingRejoin;
    private Guid seenRejoin;
    private string rejoinReply = "";
    private long followedLeaderSignal;
    private string liveConcertStatus = "";
    private string restartHash = "";
    private long restartRequestedAt;
    private bool restartPreparesArm;

    private static string ConcertParts(IEnumerable<MidiLaneKey> keys)
    {
        string list = string.Join(";", keys.Distinct().OrderBy(k => k.Track).ThenBy(k => k.Channel)
            .Select(k => FormattableString.Invariant($"{k.Track}:{k.Channel}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(list)))[..16];
    }

    private void ConcertRememberNativeSignal(long signal)
    {
        ResetConcertTake();
        if (!NativeSyncEnabled || song is null || arranged is null || string.IsNullOrEmpty(nativeLoadedSha)) return;
        liveTake = new(song, arranged, nativeLoadedSha, ConcertParts(lanes.Where(l => l.Include).Select(l => l.Key)),
            signal, 0, new BardPlaybackSettings(outputArmed, cfg.PerformanceKeys, cfg.PlaybackSpeed,
                cfg.KeyHoldMs, cfg.GuitarToneOverridesEnabled, GetActiveToneOverrides().ToDictionary()),
            plugin.Instruments.CurrentId);
    }

    private void ConcertSetNativeOrigin(long origin)
    {
        if (liveTake is { } take && ReferenceEquals(take.Song, song))
        {
            ConcertRememberNativeSignal(take.Signal);
            if (liveTake is { } started) liveTake = started with { Origin = origin };
        }
    }

    private void ResetConcertTake()
    {
        liveTake = null;
        outgoingRejoin = pendingRejoin = null;
        seenRejoin = Guid.Empty;
        rejoinReply = "";
        followedLeaderSignal = 0;
        nextConcertExchange = 0;
    }

    private bool ConcertTakePlaying(ConcertTake? take, long now) =>
        take is not null && take.Origin > 0 && now >= take.Origin &&
        !plugin.Player.IsStopPending && !plugin.Player.IsRewindPending &&
        (take.Arrangement.Notes.Count == 0 || plugin.Player.IsPlaying || plugin.Player.CompletedSuccessfully) &&
        ConcertLocalSession.Milliseconds(now - take.Origin) * take.Settings.Speed < take.Song.DurationMs;

    private bool ConcertFollowerCanRejoin(ConcertTake? take)
    {
        if (take is null || !ReferenceEquals(take.Song, song) || !ReferenceEquals(take.Arrangement, arranged) ||
            !outputArmed || !plugin.IsGameOutputSafe() || take.Arrangement.Notes.Count == 0 ||
            songFlow.HasPending || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
            !NativeLeaderSyncIsReady(out _))
            return false;
        return take.Instrument == 0 || plugin.Instruments.CurrentId == take.Instrument;
    }

    private ConcertLink? GetConcertLink()
    {
        if (!allowConcertLocal || !NativeSyncEnabled || !Plugin.ClientState.IsLoggedIn ||
            Plugin.Party.PartyLeaderIndex >= (uint)Plugin.Party.Length)
            return null;
        ulong leader = Plugin.Party[(int)Plugin.Party.PartyLeaderIndex]?.ContentId ?? 0;
        ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (me == 0 || leader == 0 || ConcertIsConductor != (me == leader)) return null;
        string stage = cfg.EnsembleGroup?.Trim() ?? "";
        var roster = NativeRoster();
        if (stage.Length is < 1 or > 50 || roster.Count < 2 || !roster.Contains(me) || !roster.Contains(leader))
            return null;

        string tag = OneClickEnsembleCue.RosterTag(roster);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NunuWindboundBard", "ConcertRecovery-v1", $"{leader:X16}-{tag}");
        return new(LocalBandSongSync.GetStageDirectory(root, stage), me, leader, tag);
    }

    public void PollConcertLive()
    {
        long now = Stopwatch.GetTimestamp();
        PollConcertRestart(now);
        PollConcertWork();
        if (!NativeSyncEnabled || !Plugin.ClientState.IsLoggedIn || (liveTake is { } old && !ReferenceEquals(old.Song, song)))
            ResetConcertTake();

        if (now >= nextConcertExchange ||
            (!allowConcertLocal && concertLink is not null) || (pendingRejoin is not null && !NativeSyncEnabled))
            UpdateConcertLink(now);

        if (liveExchange is { IsCompleted: true } exchange)
        {
            liveExchange = null;
            try
            {
                var received = exchange.GetAwaiter().GetResult();
                if (concertLink is not null && exchangeLink == concertLink) livePeers = received;
            }
            catch (Exception ex) { liveConcertStatus = "Same-PC concert status unavailable: " + ex.Message; }
        }
        if (concertLink is not { } link) return;

        var take = liveTake;
        if (link.Me != link.Leader)
            PollFollowerRejoin(link, take, now);
        else
            PollLeaderRejoin(take, now);

        if (liveExchange is not null || now < nextConcertExchange) return;
        nextConcertExchange = now + Stopwatch.Frequency / 3;
        bool isLeader = link.Leader == link.Me;
        var timing = plugin.Player.Timing;
        if (take is null || timing.OriginQpc != take.Origin) timing = default;
        string[] files = concertInventory;
        var local = new ConcertLocalPeer(link.Me, now, ConcertBuild, take?.Signal ?? 0, take?.Origin ?? 0,
            nativeLoadedSha, take?.Settings.Speed ?? cfg.PlaybackSpeed, take?.Parts ?? "",
            plugin.Instruments.CurrentId, !isLeader && followedLeaderSignal != 0 &&
                ConcertFollowerCanRejoin(take),
            isLeader ? ConcertTakePlaying(take, now) : plugin.Player.IsEnsemblePlayback,
            DescribeConcertLocal(), timing, seenRejoin, rejoinReply, outgoingRejoin,
            isLeader ? concertScanRequest : Guid.Empty, concertScanComplete, files);
        ulong[] peers = isLeader ? NativeRoster().Where(id => id != link.Me).ToArray() : [link.Leader];
        exchangeLink = link;
        liveExchange = Task.Run(() => ConcertLocalSession.Exchange(link.Directory, local, peers));
    }

    private void UpdateConcertLink(long now)
    {
        var current = GetConcertLink();
        if (current == concertLink) return;
        // Changing parties/stages must never carry a queued rejoin into the next session.
        if (concertLink is not null)
        {
            ResetConcertTake();
            ResetConcertInventory();
        }
        concertLink = current;
        livePeers.Clear();
        nextConcertExchange = current is null ? now + Stopwatch.Frequency : now;
    }

    private string DescribeConcertLocal()
    {
        if (plugin.Player.IsEnsemblePlayback) return "Playing";
        if (nativeFollowerFile is not null) return "Finding MIDI";
        if (plugin.Instruments.InProgress) return "Switching instrument";
        if (plugin.Player.IsStopPending || pendingNativeUnequip) return "Stopping";
        if (plugin.IsGameOutputSafe() && outputArmed) return "Armed, keyboard ready";
        return song is null ? "No MIDI loaded" : plugin.Player.CompletedSuccessfully ? "Finished" : "Waiting";
    }

    private bool TryFreshConcertPeer(ulong id, long now, out ConcertLocalPeer peer)
    {
        if (livePeers.TryGetValue(id, out var found) && ConcertLocalSession.Fresh(found.Written, now))
        {
            peer = found;
            return true;
        }
        peer = null!;
        return false;
    }

    private void PollFollowerRejoin(ConcertLink link, ConcertTake? take, long now)
    {
        if (!TryFreshConcertPeer(link.Leader, now, out var leader) || leader.Version != ConcertBuild)
        {
            if (pendingRejoin is not null) rejoinReply = "Cancelled: lost the conductor's local status.";
            pendingRejoin = null;
            return;
        }
        if (leader.Playing && take is not null && leader.Hash == take.Hash && followedLeaderSignal == 0 &&
            ConcertLocalSession.SameTake(leader.Signal, take.Signal))
            followedLeaderSignal = leader.Signal;
        if (pendingRejoin is { } pending)
        {
            if (!MatchesLiveRejoin(leader, pending, take) || leader.Cue?.Id != pending.Id ||
                ConcertLocalSession.Milliseconds(now - leader.Written) > 1200)
            {
                pendingRejoin = null;
                rejoinReply = "Cancelled: the take, assignments or Performance changed.";
            }
            else if (now >= pending.AtQpc)
            {
                pendingRejoin = null;
                if (GetConcertLink() != link)
                    rejoinReply = "Cancelled: the party or stage changed.";
                else if (ConcertLocalSession.Milliseconds(now - pending.AtQpc) > ConcertLocalSession.LateLimitMs)
                    rejoinReply = "Missed the bar boundary. Ask the conductor to try again.";
                else if (take is not null && plugin.Player.Rejoin(take.Arrangement.Notes,
                    take.Settings with { SendToGame = true }, plugin.IsGameOutputSafe,
                    take.Origin, pending.Origin, pending.SongMs))
                {
                    liveTake = take with { Origin = pending.Origin };
                    rejoinReply = $"Rejoined at bar {pending.Bar}.";
                }
                else rejoinReply = "Rejoin blocked by local playback state.";
            }
        }

        if (leader.Cue is not { } cue || cue.Target != link.Me || cue.Id == seenRejoin) return;
        seenRejoin = cue.Id;
        if (take is null || !ConcertLocalSession.ValidCue(cue, link.Me, take.Signal, now) ||
            !take.Arrangement.Notes.Any(note => note.TimeMs >= cue.SongMs - 0.001) ||
            !MatchesLiveRejoin(leader, cue, take))
        {
            rejoinReply = "Declined: this Bard needs the same active take, MIDI, parts and instrument, with output armed.";
            return;
        }
        pendingRejoin = cue;
        rejoinReply = $"Queued for bar {cue.Bar}.";
    }

    private bool MatchesLiveRejoin(ConcertLocalPeer leader, ConcertRejoinCue cue, ConcertTake? take) =>
        take is not null && leader.Playing && followedLeaderSignal == cue.LeaderSignal &&
        leader.Signal == cue.LeaderSignal && leader.Origin == cue.Origin &&
        take.Hash == cue.Hash && leader.Hash == cue.Hash && take.Parts == cue.Parts &&
        leader.Speed == cue.Speed && take.Settings.Speed == cue.Speed &&
        (cue.Instrument == 0 || plugin.Instruments.CurrentId == cue.Instrument) && ConcertFollowerCanRejoin(take);

    private void PollLeaderRejoin(ConcertTake? take, long now)
    {
        if (outgoingRejoin is not { } cue) return;
        if (!ConcertTakePlaying(take, now))
        {
            outgoingRejoin = null;
            liveConcertStatus = "The take ended. The queued rejoin was cancelled.";
            return;
        }
        if (TryFreshConcertPeer(cue.Target, now, out var peer) && peer.SeenCue == cue.Id)
        {
            liveConcertStatus = PartyMemberName(cue.Target) + ": " + peer.Result;
            if (now >= cue.AtQpc && !peer.Result.StartsWith("Queued", StringComparison.Ordinal) ||
                peer.Result.StartsWith("Declined", StringComparison.Ordinal) ||
                peer.Result.StartsWith("Cancelled", StringComparison.Ordinal))
                outgoingRejoin = null;
        }
        if (ConcertLocalSession.Milliseconds(now - cue.AtQpc) > 2600)
        {
            outgoingRejoin = null;
            liveConcertStatus = "Rejoin acknowledgement timed out. Check the follower or try again.";
        }
    }

    private void RequestConcertRejoin(ulong target)
    {
        long now = Stopwatch.GetTimestamp();
        if (concertLink is not { } link || link.Me != link.Leader || GetConcertLink() != link ||
            liveTake is not { } take || !ConcertTakePlaying(take, now) || take.Song.Bars is null ||
            outgoingRejoin is not null || !TryFreshConcertPeer(target, now, out var peer) ||
            peer.Version != ConcertBuild || !peer.Ready || !ConcertLocalSession.SameTake(peer.Signal, take.Signal) ||
            peer.Hash != take.Hash || peer.Speed != take.Settings.Speed)
        {
            liveConcertStatus = "Follower must be on the same PC, plugin version, take and playback speed, with the matching MIDI and Performance ready.";
            return;
        }

        var keys = partyAssignments.Where(pair => pair.Value == target).Select(pair => pair.Key).ToArray();
        if (keys.Length == 0 || ConcertParts(keys) != peer.Parts)
        {
            liveConcertStatus = "The follower's tracks do not match the conductor's explicit assignment. Prepare the track map before starting the next take.";
            return;
        }
        int instrument = GetPartyInstrumentChoices().GetValueOrDefault(target);
        if (instrument != 0 && peer.Instrument != instrument)
        {
            liveConcertStatus = $"{PartyMemberName(target)} has the wrong instrument. Equip the assigned instrument before rejoining.";
            return;
        }
        outgoingRejoin = ConcertLocalSession.Plan(take.Song.Bars, target, take.Signal, take.Origin,
            take.Settings.Speed, take.Song.DurationMs, take.Hash, peer.Parts, instrument, now);
        liveConcertStatus = outgoingRejoin is { } cue
            ? $"Requested {PartyMemberName(target)} at bar {cue.Bar}. The other bards continue."
            : "There is no remaining bar with enough lead time.";
    }

    private void BeginConcertRestart()
    {
        if (!ConcertIsConductor || string.IsNullOrEmpty(nativeLoadedSha)) return;
        if (Plugin.Party.PartyLeaderIndex >= (uint)Plugin.Party.Length ||
            Plugin.Party[(int)Plugin.Party.PartyLeaderIndex]?.ContentId !=
                PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party))
        {
            liveConcertStatus = "The Conductor must also be the FFXIV party leader to restart the whole Ensemble.";
            return;
        }
        string hash = nativeLoadedSha;
        StopPlayback();
        restartHash = hash;
        restartRequestedAt = Stopwatch.GetTimestamp();
        liveConcertStatus = "Stopping the take and releasing notes before re-preparing this song.";
    }

    private void PollConcertRestart(long now)
    {
        if (!ConcertIsConductor || restartHash != nativeLoadedSha)
        {
            restartHash = "";
            restartPreparesArm = false;
            return;
        }
        if (restartHash.Length == 0) return;
        if (restartPreparesArm)
        {
            if (concertPhase == ConcertPhase.Failed)
            {
                restartHash = "";
                restartPreparesArm = false;
                liveConcertStatus = concertStatus;
            }
            else if (concertPhase is ConcertPhase.Health or ConcertPhase.Ready)
            {
                ConductorArmParty(true);
                restartHash = "";
                restartPreparesArm = false;
                liveConcertStatus = "Wait for stage readiness, then start the same song with FFXIV's Ready Check.";
            }
            return;
        }
        double elapsed = ConcertLocalSession.Milliseconds(now - restartRequestedAt);
        if (elapsed > 16_000)
        {
            restartHash = "";
            liveConcertStatus = "Restart stopped: Performance did not finish stopping in time.";
        }
        else if (elapsed >= 1600 && !pendingNativeUnequip && !plugin.Instruments.InProgress &&
            !plugin.Player.IsPlaying && !plugin.Player.IsStopPending && !songFlow.HasPending)
        {
            restartPreparesArm = true;
            ConcertPrepareOrchestra();
        }
    }

    private void DrawConcertLocalOptIn()
    {
        ImGui.Checkbox(Loc.T("Enable same-PC concert recovery for this session"), ref allowConcertLocal);
        if (allowConcertLocal && !ConcertIsConductor && !string.IsNullOrEmpty(rejoinReply))
            ImGui.TextWrapped(rejoinReply);
    }

    private void DrawConcertLive()
    {
        var capture = plugin.StudioCapture.Snapshot;
        var next = SmartPlaylistManager.PeekNext(GetActiveSmartPlaylist(), cfg.Playlist, activePlaylist);
        ImGui.TextWrapped($"Current: {(song is null ? "No song" : Path.GetFileName(loaded))}    Next: {next}");
        ImGui.TextWrapped(capture.Recording ? "Capture Studio recording" :
            capture.Connected ? "Capture Studio connected" : "Capture Studio disconnected");
        DrawConcertLocalOptIn();

        ImGui.BeginDisabled(song is null || restartHash.Length > 0);
        if (ImGui.Button(Loc.T("Resync Ensemble (restart song)"))) ImGui.OpenPopup("##RestartEnsemble");
        ImGui.EndDisabled();
        if (ImGui.BeginPopup("##RestartEnsemble"))
        {
            ImGui.TextWrapped(Loc.T("Stop this take for the whole party and prepare the same song again?"));
            if (ImGui.Button(Loc.T("Stop and prepare")))
            {
                BeginConcertRestart();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Cancel"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Export Timing Report"))) ImGui.SetClipboardText(ConcertTimingReport());
        if (!string.IsNullOrEmpty(liveConcertStatus)) ImGui.TextWrapped(liveConcertStatus);
        if (allowConcertLocal)
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var member in PartyRoster.GetChoices(Plugin.Objects, Plugin.Party))
            {
                if (member.IsLocal)
                {
                    ImGui.TextUnformatted($"{member.Name}: {DescribeConcertLocal()}");
                    continue;
                }
                bool seen = TryFreshConcertPeer(member.ContentId, now, out var peer);
                string state = !seen ? "No recent same-PC status" : peer.Version != ConcertBuild
                    ? "Different plugin version" : peer.Hash != nativeLoadedSha ? "Different MIDI" : peer.State;
                if (seen && liveTake is { Origin: > 0 } playing && peer.Hash == playing.Hash && peer.Origin > 0 &&
                    ConcertLocalSession.SameTake(peer.Signal, playing.Signal))
                {
                    double offset = ConcertLocalSession.Milliseconds(peer.Origin - playing.Origin);
                    state += FormattableString.Invariant($" · clock {offset:+0;-0;0} ms");
                    if (peer.Timing.Count > 0)
                        state += FormattableString.Invariant($", last note {peer.Timing.LastMs:0} ms late");
                }
                ImGui.TextUnformatted($"{member.Name}: {state}");
                if (ConcertTakePlaying(liveTake, now) && seen)
                {
                    ImGui.SameLine();
                    ImGui.BeginDisabled(!peer.Ready || outgoingRejoin is not null);
                    if (ImGui.SmallButton($"Rejoin next bar##concert-{member.ContentId}"))
                        RequestConcertRejoin(member.ContentId);
                    ImGui.EndDisabled();
                }
            }
        }
        ImGui.BeginDisabled(!ConcertAllReady() || plugin.Player.IsPlaying || restartHash.Length > 0);
        if (ImGui.Button(Loc.T("Start via FFXIV Ensemble Ready Check"))) CueEnsemble();
        ImGui.EndDisabled();
        DrawConcertPreflight();
        DrawConcertRehearsal();
        DrawConcertPacks();
    }

    private string ConcertTimingReport()
    {
        var take = liveTake;
        var timestamp = Stopwatch.GetTimestamp();
        var text = new StringBuilder($"Windbound {ConcertBuild} | {DateTimeOffset.UtcNow:O}\n");
        text.AppendLine($"MIDI: {Path.GetFileName(loaded)} | {nativeLoadedSha}");
        if (take is null) return text.AppendLine("No native Ensemble take observed locally.").ToString();
        text.AppendLine("Dispatch delay in milliseconds, relative to each bard's scheduled notes.");
        foreach (var member in PartyRoster.GetChoices(Plugin.Objects, Plugin.Party))
        {
            if (!member.IsLocal && !TryFreshConcertPeer(member.ContentId, timestamp, out _))
            {
                text.AppendLine($"{member.Name}: no same-PC timing report");
                continue;
            }
            var peer = member.IsLocal ? null : livePeers[member.ContentId];
            if (peer is not null && (peer.Hash != take.Hash || !ConcertLocalSession.SameTake(peer.Signal, take.Signal)))
            {
                text.AppendLine($"{member.Name}: no timing for this take");
                continue;
            }
            var timing = peer?.Timing ?? plugin.Player.Timing;
            long origin = peer?.Origin ?? take.Origin;
            long signal = peer?.Signal ?? take.Signal;
            if (timing.Count == 0)
            {
                text.AppendLine($"{member.Name}: no note-ons recorded yet");
                continue;
            }
            text.AppendLine(FormattableString.Invariant($"{member.Name}: {timing.Count} notes; start clock offset {ConcertLocalSession.Milliseconds(origin - take.Origin):+0.0;-0.0;0.0}; native signal offset {ConcertLocalSession.Milliseconds(signal - take.Signal):+0.0;-0.0;0.0}; first {timing.FirstMs:0.0}; latest {timing.LastMs:0.0}; mean {timing.MeanMs:0.0}; worst {timing.WorstMs:0.0}"));
        }
        return text.ToString();
    }
}
