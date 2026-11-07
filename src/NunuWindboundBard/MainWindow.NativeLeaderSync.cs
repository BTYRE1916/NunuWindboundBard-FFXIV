using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Conductor song/file & instrument controls modelled on the MidiBard workflow.
/// Only metadata/commands travel over FFXIV party chat. Neither command can
/// START playback: the native FFXIV ensemble callback remains the only trigger.
/// All FFXIV actions are made on Framework.Update, not the chat event handler.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class NativeLeaderSession
    {
        public required string Nonce;
        public required HashSet<ulong> Expected;
        public HashSet<ulong> Ready { get; } = new();
        public bool Failed;
        public string Failure = "";
        public long CreatedQpc;
        public Dictionary<ulong, int> Instruments { get; } = new();
        public bool Complete => !Failed && Expected.SetEquals(Ready);
    }

    private sealed class NativeFollowerFile
    {
        public required GamePartyCue Cue;
        public required List<string> Candidates;
        public int SearchCursor;
        public bool LoadRequested;
        public bool MapApplied;
        public bool Failed;
        public long LastReplyQpc;
        public long CreatedQpc;
    }

    private sealed class NativeFollowerTracks
    {
        public required GamePartyCue Cue;
        public long CreatedQpc;
        public long LastReplyQpc;
    }

    private sealed class NativeFollowerInstrument
    {
        public required string Nonce;
        public int TargetId;
        public long CreatedQpc;
        public long LastReplyQpc;
        public bool Failed;
    }

    private NativeLeaderSession? nativeLeaderFile;
    private NativeLeaderSession? nativeLeaderInstruments;
    private NativeLeaderSession? nativeLeaderTracks;
    private NativeFollowerTracks? nativeFollowerTracks;
    private NativeFollowerFile? nativeFollowerFile;
    private NativeFollowerInstrument? nativeFollowerInstrument;
    private string nativeLoadedSha = "";
    private string nativeFileSyncStatus = "No conductor MIDI selection received yet.";
    private string nativeTracksSyncStatus = "No conductor Set Party Tracks received yet.";
    private string nativeStopStatus = "Leader STOP has not been requested.";
    private bool pendingNativeUnequip;
    private bool nativeUnequipStatusActive;
    private bool nativeConductorTrackMapExplicit;
    private readonly HashSet<string> processedNativeTrackNonces = new(StringComparer.Ordinal);
    private readonly HashSet<string> processedNativeStowNonces = new(StringComparer.Ordinal);
    private string nativeInstrumentSyncStatus = "No conductor instrument SET received yet.";
    private string nativeLastFileSendError = "";
    private readonly HashSet<string> processedNativeFileNonces = new(StringComparer.Ordinal);
    private readonly HashSet<string> processedNativeInstrumentNonces = new(StringComparer.Ordinal);

    private bool NativeSyncEnabled => cfg.GameEnsembleEnabled && !cfg.GameEnsembleOneClick &&
        cfg.NativeLeaderSyncEnabled && cfg.EnsembleMode != (int)EnsembleRole.Solo;

    private static string NewNativeNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private IReadOnlyList<ulong> NativeRoster() => PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
        .Select(member => member.ContentId).Where(id => id != 0).Distinct().ToArray();

    private int NativeTargetSlot(ulong? targetId)
    {
        if (!targetId.HasValue) return 0;
        var ordered = NativeRoster().OrderBy(x => x).ToArray();
        int slot = Array.IndexOf(ordered, targetId.Value) + 1;
        if (slot is < 1 or > 15) throw new InvalidOperationException("Target isn't in the current identifiable party.");
        return slot;
    }

    private bool NativeCommandTargetsMe(GamePartyCue cue)
    {
        if (cue.TargetSlot == 0) return true;
        // A target index is safe only when all clients recognise the same roster.
        // Reject rather than switching a different account if the party changed.
        if (cue.RosterTag == "00000000" || !string.Equals(cue.RosterTag,
            OneClickEnsembleCue.RosterTag(NativeRoster()), StringComparison.OrdinalIgnoreCase)) return false;
        ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        return me != 0 && NativeTargetSlot(me) == cue.TargetSlot;
    }

    private NativeLeaderSession CreateLeaderSession(string nonce, ulong? onlyMember = null)
    {
        ulong localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        var members = NativeRoster();
        if (localId == 0 || members.Count < 2 || !members.Contains(localId))
            throw new InvalidOperationException("Join a party with at least two identifiable characters first.");
        if (onlyMember.HasValue && (onlyMember.Value == localId || !members.Contains(onlyMember.Value)))
            throw new InvalidOperationException("Selected retry member isn't a different Bard in this party.");
        return new NativeLeaderSession
        {
            Nonce = nonce,
            Expected = members.Where(id => id != localId && (!onlyMember.HasValue || id == onlyMember.Value)).ToHashSet(),
            CreatedQpc = Stopwatch.GetTimestamp()
        };
    }

    private string NativeLeaderProgress(NativeLeaderSession? session, string kind)
    {
        if (session == null) return $"{kind}: not sent";
        if (session.Failed) return $"{kind}: FAILED — {session.Failure}";
        if (session.Complete) return $"{kind}: READY ({session.Ready.Count}/{session.Expected.Count})";
        return $"{kind}: waiting ({session.Ready.Count}/{session.Expected.Count})";
    }

    private void DrawNativeLeaderSync(EnsembleRole role)
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.98f, .60f, .84f, 1f), Loc.T("CONDUCTOR FILE + INSTRUMENT SYNC"));
        bool sync = cfg.NativeLeaderSyncEnabled;
        if (ImGui.Checkbox(Loc.T("Automatically follow the party leader's MIDI and instrument choices##nativeSync"), ref sync))
        {
            cfg.NativeLeaderSyncEnabled = sync;
            if (!sync) CancelNativeLeaderSync("Party leader synchronisation disabled.");
            cfg.Save();
        }
        if (!sync)
        {
            ImGui.TextDisabled(Loc.T("Native Ready Check still works, but each Bard must choose its own file and instrument."));
            return;
        }
        if (role == EnsembleRole.Conductor)
        {
            ImGui.TextWrapped(Loc.T("Selecting a song automatically tells the party to load the identical MIDI " +
                "and apply any conductor track assignments. Everyone must have the MIDI file locally."));
            if (ImGui.Button(Loc.T("SYNC SELECTED MIDI TO PARTY"))) BroadcastNativeFile();
            DrawConductorInstrumentPlan();
            if (ImGui.Button(Loc.T("SET PARTY INSTRUMENTS — auto-switch everyone"))) SetNativePartyInstruments();
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("SET PARTY TRACKS — assign every Bard"))) SetNativePartyTracks();
            ImGui.TextDisabled(Loc.T("Assign MIDI tracks in Party-Roster Track Assignments below, then press SET PARTY TRACKS."));
            if (ImGui.Button(Loc.T("STOP ENSEMBLE + UNEQUIP ALL"))) StopPlayback();
            ImGui.TextDisabled(Loc.T("STOP releases notes and closes Performance for leader and every follower."));
            ImGui.TextColored(nativeLeaderTracks?.Failed == true ? new Vector4(1f,.59f,.42f,1f) :
                new Vector4(.60f,.94f,.81f,1f), Loc.T(NativeLeaderProgress(nativeLeaderTracks, "Tracks")));
            ImGui.TextDisabled(Loc.T("SET changes instruments only. START ENSEMBLE below uses FFXIV's native Ready Check."));
            ImGui.TextColored(nativeLeaderFile?.Failed == true ? new Vector4(1f,.59f,.42f,1f) :
                new Vector4(.60f,.94f,.81f,1f), Loc.T(NativeLeaderProgress(nativeLeaderFile, "MIDI")));
            ImGui.TextColored(nativeLeaderInstruments?.Failed == true ? new Vector4(1f,.59f,.42f,1f) :
                new Vector4(.60f,.94f,.81f,1f), Loc.T(NativeLeaderProgress(nativeLeaderInstruments, "Instruments")));
        }
        else if (role == EnsembleRole.Follower)
        {
            ImGui.TextWrapped(Loc.T("FOLLOWER: Song choice and SET instrument commands apply automatically. " +
                "No clicking in your client is needed for each new song."));
            ImGui.TextDisabled(Loc.T("Keep ARM enabled for musical output. Auto-Accept Ready Check is separately enabled above."));
        }
        ImGui.TextWrapped(Loc.T("File: " + nativeFileSyncStatus));
        ImGui.TextWrapped(Loc.T("Tracks: " + nativeTracksSyncStatus));
        ImGui.TextWrapped(Loc.T("Instrument: " + nativeInstrumentSyncStatus));
        ImGui.TextWrapped(Loc.T("STOP / Unequip: " + nativeStopStatus));
        if (role == EnsembleRole.Follower)
            ImGui.TextDisabled(Loc.T("If the conductor switched locally but you did not, check the receiver diagnostics below."));
        if (ImGui.TreeNode(Loc.T("Party command receiver diagnostics##nativeSender")))
        {
            ImGui.TextWrapped(Loc.T($"Character role: {role}; Native Game Ensemble: {cfg.GameEnsembleEnabled}; " +
                $"Follow party leader: {cfg.NativeLeaderSyncEnabled}; Party receiver active: {plugin.PartyChat.ReceiveEnabled}"));
            ImGui.TextWrapped(Loc.T(plugin.PartyChat.Diagnostics));
            if (ImGui.SmallButton(Loc.T("Copy party-command report")))
                ImGui.SetClipboardText($"Windbound 0.18.2 / {role}\nNative Game Ensemble: {cfg.GameEnsembleEnabled}; " +
                    $"Leader sync: {cfg.NativeLeaderSyncEnabled}\n" +
                    plugin.PartyChat.Diagnostics + "\nFile: " + nativeFileSyncStatus +
                    "\nTracks: " + nativeTracksSyncStatus +
                    "\nInstrument: " + nativeInstrumentSyncStatus +
                    "\nSTOP: " + nativeStopStatus +
                    "\nSwitcher: " + plugin.Instruments.Status);
            ImGui.TreePop();
        }
    }

    /// <summary>Called only after a MIDI has been successfully loaded locally.
    /// MIDI bytes are not sent: clients independently identify the same file.</summary>
    private void OnNativeConductorSongLoaded()
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Conductor) return;
        ConcertOnMidiChanged(); // no stale stage READY when selecting a new MIDI
        BroadcastNativeFile();
    }

    private void BroadcastNativeFile(ulong? onlyMember = null)
    {
        try
        {
            if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Conductor)
                throw new InvalidOperationException("Select Conductor and native Game Ensemble first.");
            if (string.IsNullOrWhiteSpace(loaded) || song is null || arranged is null)
                throw new InvalidOperationException("Load a MIDI first.");
            string nonce = NewNativeNonce();
            var session = CreateLeaderSession(nonce, onlyMember);
            var members = NativeRoster();
            string packed = OneClickEnsembleCue.EncodeAssignments(lanes.Select(l => l.Key).ToList(),
                partyAssignments, members, out string rosterTag);
            int index = cfg.Playlist.FindIndex(p => string.Equals(p, loaded, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException("Add the MIDI to the active Ensemble playlist before syncing it.");
            string message;
            try { message = GamePartyCueProtocol.NativeFile(index, loaded, nonce, rosterTag, packed,
                NativeTargetSlot(onlyMember)); }
            catch (InvalidOperationException)
            {
                // Very long arrangements may exceed the chat limit. At least
                // send song selection; existing local lane presets remain in use.
                message = GamePartyCueProtocol.NativeFile(index, loaded, nonce,
                    onlyMember.HasValue ? OneClickEnsembleCue.RosterTag(NativeRoster()) : "00000000",
                    "-", NativeTargetSlot(onlyMember));
                nativeFileSyncStatus = "Track map too large for chat; syncing MIDI only. Select each part locally.";
            }
            plugin.PartyChat.Enabled = true; // usable immediately after enabling native mode
            if (!plugin.PartyChat.TryBroadcast(message, out string detail))
                throw new InvalidOperationException(detail);
            nativeLeaderFile = session;
            nativeLastFileSendError = "";
            nativeFileSyncStatus = "Song selection sent: " + Path.GetFileName(loaded) + ". Waiting for each Bard to load it.";
            nativeInstrumentSyncStatus = "Use SET Party Instruments after selecting the MIDI.";
            if (!onlyMember.HasValue)
            {
                nativeLeaderInstruments = null; // the full new MIDI may change instruments
                nativeLeaderTracks = null;
                nativeConductorTrackMapExplicit = false;
            }
        }
        catch (Exception ex)
        {
            nativeLeaderFile = null;
            nativeLastFileSendError = "MIDI sync not sent: " + ex.Message;
            nativeFileSyncStatus = nativeLastFileSendError;
        }
    }

    private void SetNativePartyInstruments(ulong? onlyMember = null)
    {
        try
        {
            if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Conductor)
                throw new InvalidOperationException("Select Conductor and native Game Ensemble first.");
            if (song is null || string.IsNullOrEmpty(loaded))
                throw new InvalidOperationException("Load a MIDI to choose instruments for the party.");
            if (plugin.Player.IsPlaying || plugin.Instruments.InProgress)
                throw new InvalidOperationException("STOP playback or finish the current instrument change first.");
            var choices = GetPartyInstrumentChoices();
            var members = NativeRoster();
            string packed = PartyInstrumentPlan.Encode(choices, members, out string rosterTag);
            string nonce = NewNativeNonce();
            var session = CreateLeaderSession(nonce, onlyMember);
            foreach (var (id, instrument) in choices) session.Instruments[id] = instrument;
            string command = GamePartyCueProtocol.NativeSetInstruments(nonce, rosterTag, packed,
                NativeTargetSlot(onlyMember));
            plugin.PartyChat.Enabled = true;
            if (!plugin.PartyChat.TryBroadcast(command, out string reason))
                throw new InvalidOperationException(reason);
            nativeLeaderInstruments = session;
            nativeInstrumentSyncStatus = "SET sent. Windbound is automatically equipping each party member's instrument.";
            ulong localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
            int localInstrument = choices.GetValueOrDefault(localId);
            if (!onlyMember.HasValue && localInstrument != 0 && !plugin.Instruments.IsReadyFor(localInstrument))
                plugin.Instruments.Request(localInstrument);
        }
        catch (Exception ex)
        {
            if (nativeLeaderInstruments != null)
            {
                nativeLeaderInstruments.Failed = true;
                nativeLeaderInstruments.Failure = ex.Message;
            }
            nativeInstrumentSyncStatus = "SET failed: " + ex.Message;
        }
    }

    /// <summary>Explicit per-track conductor SEND without changing the selected
    /// MIDI. Works for octets on one PC and mixed Linux/Windows FFXIV parties.</summary>
    private void SetNativePartyTracks(ulong? onlyMember = null)
    {
        try
        {
            if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Conductor)
                throw new InvalidOperationException("Choose Native Ensemble Conductor first.");
            if (string.IsNullOrWhiteSpace(loaded) || song is null || arranged is null)
                throw new InvalidOperationException("Load the Ensemble MIDI before setting tracks.");
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("Stop playback before changing party tracks.");
            if (nativeLeaderFile is { Complete: false })
                throw new InvalidOperationException("Wait until every Bard has loaded the conductor MIDI.");
            string nonce = NewNativeNonce();
            var session = CreateLeaderSession(nonce, onlyMember);
            string packed = OneClickEnsembleCue.EncodeAssignments(lanes.Select(l => l.Key).ToList(),
                partyAssignments, NativeRoster(), out string rosterTag, explicitMap: true);
            string command = GamePartyCueProtocol.NativeSetTracks(
                GamePartyCueProtocol.GetHash(loaded), nonce, rosterTag, packed,
                NativeTargetSlot(onlyMember));
            plugin.PartyChat.Enabled = true;
            if (!plugin.PartyChat.TryBroadcast(command, out string reason))
                throw new InvalidOperationException(reason);
            // Apply to local conductor only after the message was submitted.
            nativeLeaderTracks = session;
            nativeConductorTrackMapExplicit = true;
            if (!onlyMember.HasValue) ApplyPartyAssignmentsToLocal();
            nativeTracksSyncStatus = $"Set {partyAssignments.Count} track assignments. " +
                $"Waiting for {session.Expected.Count} performer acknowledgement(s).";
            assignmentStatus = nativeTracksSyncStatus;
        }
        catch (Exception ex)
        {
            nativeTracksSyncStatus = "Could not SET Party Tracks: " + ex.Message;
            assignmentStatus = nativeTracksSyncStatus;
        }
    }

    /// <summary>Validate the exact MIDI bytes and roster before applying the
    /// conductor's lane assignments. Report READY only after rebuilding the song.</summary>
    private void ReceiveNativeTracks(GamePartyCue cue)
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Follower ||
            !NativeCommandTargetsMe(cue)) return;
        if (!processedNativeTrackNonces.Add(cue.Nonce)) return;
        if (processedNativeTrackNonces.Count > 128) processedNativeTrackNonces.Clear();
        try
        {
            if (nativeFollowerFile != null || songFlow.HasPending || plugin.Player.IsPlaying ||
                plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("MIDI isn't idle or finished loading.");
            if (song is null || arranged is null || string.IsNullOrWhiteSpace(loaded) ||
                !string.Equals(nativeLoadedSha, cue.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MIDI mismatch; sync conductor's selected MIDI first.");
            if (!OneClickEnsembleCue.TryDecodeAssignments(cue.PackedAssignments,
                cue.RosterTag, lanes.Select(l => l.Key).ToList(), NativeRoster(),
                out var assignments, out var error))
                throw new InvalidOperationException(error);
            partyAssignments.Clear();
            foreach (var (lane, contentId) in assignments) partyAssignments[lane] = contentId;
            partyAssignmentsReceivedForCurrentMidi = true; // yes, even all-silent track map
            ApplyPartyAssignmentsToLocal();
            nativeFollowerTracks = new NativeFollowerTracks { Cue = cue, CreatedQpc = Stopwatch.GetTimestamp() };
            nativeTracksSyncStatus = $"Received conductor track map: " +
                $"{lanes.Count(l => l.Include)} lane(s) for my Bard; reporting READY.";
            assignmentStatus = nativeTracksSyncStatus;
        }
        catch (Exception ex)
        {
            nativeFollowerTracks = null;
            nativeTracksSyncStatus = "SET Party Tracks rejected: " + ex.Message;
            plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeTracksFailed(cue.Nonce, "TRACKMAP"), out _);
        }
    }

    /// <summary>Leader-only STOP shared over party chat. Each client waits for
    /// its own note-off processing before requesting a native Performance close.</summary>
    private void NativeConductorStopAndUnequip()
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Conductor) return;
        pendingNativeUnequip = true;
        plugin.PartyChat.Enabled = true;
        try
        {
            string cue = GamePartyCueProtocol.NativeStopAndUnequip(NewNativeNonce());
            if (!plugin.PartyChat.TryBroadcast(cue, out string detail))
                nativeStopStatus = "Local STOP + unequip queued; remote STOP NOT delivered: " + detail;
            else nativeStopStatus = "STOP + unequip submitted to party; waiting for local notes to release.";
        }
        catch (Exception ex)
        {
            nativeStopStatus = "Local STOP + unequip queued; party signal failed: " + ex.Message;
        }
        CancelNativeLeaderSync("Conductor STOP: previous file, instrument and track readiness reset.");
        gameStartGate.Cancel("Conductor STOP + stow; require a new native ensemble cue.");
    }

    private void ReceiveNativeStopAndUnequip(GamePartyCue cue)
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Follower) return;
        if (!processedNativeStowNonces.Add(cue.Nonce)) return;
        if (processedNativeStowNonces.Count > 128) processedNativeStowNonces.Clear();
        CancelOneClickInstrumentPreparation();
        CancelPendingInstrumentStart();
        plugin.Instruments.Cancel();
        plugin.Player.Stop();
        songFlow.Cancel();
        gameStartGate.Cancel("Conductor STOP + unequip received.");
        pendingNativeUnequip = true;
        nativeFollowerTracks = null;
        nativeFollowerInstrument = null;
        // The MIDI file remains loaded; only pending commands and readiness
        // are invalidated. The conductor must prepare a fresh SET/ready-check.
        CancelNativeLeaderSync("Conductor stopped the ensemble; prepare a new cue.");
        nativeStopStatus = "Conductor STOP received. Notes releasing, then unequipping Performance.";
        gameEnsembleStatus = nativeStopStatus;
    }

    private void ReceiveNativeFile(GamePartyCue cue)
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Follower ||
            !NativeCommandTargetsMe(cue)) return;
        if (!processedNativeFileNonces.Add(cue.Nonce)) return;
        if (processedNativeFileNonces.Count > 200) processedNativeFileNonces.Clear();
        expectedPartyMidiHash = cue.Hash; // prevent a wrong MIDI from auto-starting
        nativeFollowerFile = new NativeFollowerFile
        {
            Cue = cue,
            Candidates = BuildNativeSongCandidates(cue.PlaylistIndex),
            CreatedQpc = Stopwatch.GetTimestamp()
        };
        nativeFileSyncStatus = $"Conductor selected MIDI {cue.PlaylistIndex + 1}. Looking for its matching hash...";
    }

    private List<string> BuildNativeSongCandidates(int selectedIndex)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path)) candidates.Add(path);
        }
        if (selectedIndex >= 0 && selectedIndex < cfg.Playlist.Count) Add(cfg.Playlist[selectedIndex]);
        Add(loaded);
        foreach (string path in cfg.Playlist) Add(path);
        // Cross-platform path roots and playlist order may differ. Look in ALL
        // saved local profiles, a few files per framework tick, without blocking.
        foreach (var profile in cfg.SavedPlaylists)
            foreach (var path in profile.Files) Add(path);
        return candidates;
    }

    private void ApplyNativeFileTrackMap(GamePartyCue cue)
    {
        if (cue.PackedAssignments == "-")
        {
            // Without a conductor map, preserve follower's manually saved parts.
            partyAssignmentsReceivedForCurrentMidi = false;
            partyAssignments.Clear();
            ApplyPartyAssignmentsToLocal();
            return;
        }
        var roster = NativeRoster();
        if (!OneClickEnsembleCue.TryDecodeAssignments(cue.PackedAssignments, cue.RosterTag,
            lanes.Select(x => x.Key).ToList(), roster, out var assignments, out string error))
            throw new InvalidOperationException("Conductor party-track map rejected: " + error);
        partyAssignments.Clear();
        foreach (var (key, id) in assignments) partyAssignments[key] = id;
        partyAssignmentsReceivedForCurrentMidi = true;
        ApplyPartyAssignmentsToLocal();
        assignmentStatus = $"Party tracks synchronised: {lanes.Count(x => x.Include)} lane(s) for my Bard.";
    }

    private void ReceiveNativeInstruments(GamePartyCue cue)
    {
        if (!NativeSyncEnabled || cfg.EnsembleMode != (int)EnsembleRole.Follower ||
            !NativeCommandTargetsMe(cue)) return;
        if (!processedNativeInstrumentNonces.Add(cue.Nonce)) return;
        if (processedNativeInstrumentNonces.Count > 200) processedNativeInstrumentNonces.Clear();
        try
        {
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                plugin.Instruments.InProgress)
                throw new InvalidOperationException("STOP the current song or instrument transition before SET.");
            var roster = NativeRoster();
            if (!PartyInstrumentPlan.TryDecode(cue.PackedInstruments, cue.RosterTag,
                roster, out var choices, out string problem))
                throw new InvalidOperationException(problem);
            ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
            if (!choices.TryGetValue(me, out int selected))
                throw new InvalidOperationException("Local Bard is missing from the conductor's instrument map.");
            nativeFollowerInstrument = new NativeFollowerInstrument
            {
                Nonce = cue.Nonce, TargetId = selected, CreatedQpc = Stopwatch.GetTimestamp()
            };
            if (selected != 0 && !plugin.Instruments.IsReadyFor(selected))
                plugin.Instruments.Request(selected);
            nativeInstrumentSyncStatus = "Auto-switching to " + InstrumentCatalog.Display(selected) + "...";
        }
        catch (Exception ex)
        {
            nativeInstrumentSyncStatus = "Could not apply SET: " + ex.Message;
            nativeFollowerInstrument = null;
            plugin.PartyChat.TryReply(GamePartyCueProtocol.Failed(cue.Nonce, "SETFAILED"), out _);
        }
    }

    private void OnNativeLeaderReply(GamePartyCue cue, ulong senderId)
    {
        if (cfg.EnsembleMode != (int)EnsembleRole.Conductor) return;
        if (cue.Kind is GamePartyCueKind.NativeFileReady or GamePartyCueKind.NativeFileFailed)
        {
            var session = nativeLeaderFile;
            if (session == null || cue.Nonce != session.Nonce || !session.Expected.Contains(senderId)) return;
            if (cue.Kind == GamePartyCueKind.NativeFileFailed)
            {
                session.Failed = true;
                session.Failure = PartyMemberName(senderId) + " couldn't load the MIDI (" + cue.PackedInstruments + ").";
            }
            else session.Ready.Add(senderId);
            nativeFileSyncStatus = NativeLeaderProgress(session, "MIDI");
            return;
        }
        if (cue.Kind is GamePartyCueKind.NativeTracksReady or GamePartyCueKind.NativeTracksFailed)
        {
            var session = nativeLeaderTracks;
            if (session is null || cue.Nonce != session.Nonce || !session.Expected.Contains(senderId)) return;
            if (cue.Kind == GamePartyCueKind.NativeTracksFailed)
            {
                session.Failed = true;
                session.Failure = PartyMemberName(senderId) + " rejected the track map (" + cue.PackedInstruments + ").";
            }
            else session.Ready.Add(senderId);
            nativeTracksSyncStatus = NativeLeaderProgress(session, "Tracks");
            return;
        }
        if (cue.Kind is GamePartyCueKind.Ready or GamePartyCueKind.Failed)
        {
            var session = nativeLeaderInstruments;
            if (session == null || cue.Nonce != session.Nonce || !session.Expected.Contains(senderId)) return;
            if (cue.Kind == GamePartyCueKind.Failed)
            {
                session.Failed = true;
                session.Failure = PartyMemberName(senderId) + " couldn't equip an instrument (" + cue.PackedInstruments + ").";
            }
            else if (int.TryParse(cue.PackedInstruments, out int equipped) &&
                equipped == session.Instruments.GetValueOrDefault(senderId))
                session.Ready.Add(senderId);
            nativeInstrumentSyncStatus = NativeLeaderProgress(session, "Instruments");
        }
    }

    private string PartyMemberName(ulong id) => PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
        .FirstOrDefault(x => x.ContentId == id)?.Name ?? "Bard";

    /// <summary>Framework update: a bounded number of local disk hashes plus
    /// native switch completion. Never plays notes and never changes windows.</summary>
    public void PollNativeConductorSync()
    {
        // STOP/unequip must still finish if a user disables Native Ensemble
        // while keys are being released; don't strand an active instrument.
        if (pendingNativeUnequip && !plugin.Player.IsPlaying && !plugin.Player.IsStopPending &&
            !plugin.Player.IsRewindPending)
        {
            pendingNativeUnequip = false;
            plugin.Instruments.RequestUnequip();
            nativeUnequipStatusActive = true;
            nativeStopStatus = plugin.Instruments.Status;
        }
        if (nativeUnequipStatusActive)
        {
            nativeStopStatus = plugin.Instruments.Status;
            if (!plugin.Instruments.InProgress) nativeUnequipStatusActive = false;
        }
        if (!NativeSyncEnabled)
        {
            nativeFollowerFile = null;
            nativeFollowerInstrument = null;
            nativeFollowerTracks = null;
            return;
        }
        var now = Stopwatch.GetTimestamp();
        if (nativeFollowerTracks is { } tracks)
        {
            if ((now - tracks.CreatedQpc) / (double)Stopwatch.Frequency > 20)
            {
                nativeTracksSyncStatus = "Couldn't send track READY; reply timed out.";
                nativeFollowerTracks = null;
            }
            else if (tracks.LastReplyQpc == 0 || now - tracks.LastReplyQpc > Stopwatch.Frequency)
            {
                tracks.LastReplyQpc = now;
                if (plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeTracksReady(tracks.Cue.Nonce), out _))
                {
                    nativeTracksSyncStatus = $"Track READY: {lanes.Count(l => l.Include)} part(s) assigned locally.";
                    nativeFollowerTracks = null;
                }
            }
        }
        if (nativeFollowerFile is { } file)
        {
            if ((now - file.CreatedQpc) / (double)Stopwatch.Frequency > 40)
            {
                FailFollowerFile(file, "TIMEOUT", "MIDI search/load timed out.");
            }
            else if (!songFlow.HasPending && !plugin.Player.IsStopPending &&
                !string.IsNullOrEmpty(nativeLoadedSha) &&
                string.Equals(nativeLoadedSha, file.Cue.Hash, StringComparison.OrdinalIgnoreCase) &&
                song is not null && arranged is not null)
            {
                try
                {
                    if (!file.MapApplied)
                    {
                        ApplyNativeFileTrackMap(file.Cue);
                        file.MapApplied = true;
                    }
                    if (file.LastReplyQpc == 0 || now - file.LastReplyQpc > Stopwatch.Frequency)
                    {
                        file.LastReplyQpc = now;
                        if (plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeFileReady(file.Cue.Nonce), out _))
                        {
                            nativeFileSyncStatus = "Matching MIDI loaded and track assignments applied: " + Path.GetFileName(loaded);
                            nativeFollowerFile = null;
                        }
                    }
                }
                catch (Exception ex) { FailFollowerFile(file, "TRACKMAP", ex.Message); }
            }
            else if (!file.LoadRequested)
            {
                int inspected = 0;
                while (inspected++ < 4 && file.SearchCursor < file.Candidates.Count)
                {
                    string path = file.Candidates[file.SearchCursor++];
                    if (!File.Exists(path)) continue;
                    try
                    {
                        if (!string.Equals(GamePartyCueProtocol.GetHash(path), file.Cue.Hash,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        file.LoadRequested = true;
                        LoadFromUser(path); // stops/reloads on the existing framework transition path
                        break;
                    }
                    catch (Exception) { /* Ignore unreadable individual candidate; try the next. */ }
                }
                if (!file.LoadRequested && file.SearchCursor >= file.Candidates.Count)
                    FailFollowerFile(file, "NOTFOUND", "The conductor's exact MIDI isn't in any local saved playlist.");
            }
            else if (!songFlow.HasPending && !plugin.Player.IsPlaying &&
                !string.Equals(nativeLoadedSha, file.Cue.Hash, StringComparison.OrdinalIgnoreCase))
                FailFollowerFile(file, "LOADERROR", "Found MIDI failed to load in Windbound.");
        }
        if (nativeFollowerInstrument is { } instrument)
        {
            if ((now - instrument.CreatedQpc) / (double)Stopwatch.Frequency > 35 ||
                (instrument.TargetId != 0 && plugin.Instruments.HasFailed &&
                 !plugin.Instruments.IsReadyFor(instrument.TargetId)))
            {
                if (!instrument.Failed)
                {
                    instrument.Failed = true;
                    nativeInstrumentSyncStatus = "Instrument SET failed: " + plugin.Instruments.Status;
                    plugin.PartyChat.TryReply(GamePartyCueProtocol.Failed(instrument.Nonce, "SWITCH"), out _);
                }
                nativeFollowerInstrument = null;
            }
            else if (!plugin.Instruments.InProgress &&
                (instrument.TargetId == 0 || plugin.Instruments.IsReadyFor(instrument.TargetId)) &&
                plugin.IsGameOutputSafe() &&
                (instrument.LastReplyQpc == 0 || now - instrument.LastReplyQpc > Stopwatch.Frequency))
            {
                instrument.LastReplyQpc = now;
                if (plugin.PartyChat.TryReply(GamePartyCueProtocol.Ready(instrument.Nonce, instrument.TargetId), out _))
                {
                    nativeInstrumentSyncStatus = "Instrument ready: " + InstrumentCatalog.Display(instrument.TargetId);
                    nativeFollowerInstrument = null;
                }
            }
        }
        if (nativeLeaderFile is { Complete: false, Failed: false } fileSession &&
            (now - fileSession.CreatedQpc) / (double)Stopwatch.Frequency > 40)
        {
            fileSession.Failed = true;
            fileSession.Failure = "Not all party members acknowledged the MIDI. Resend Sync MIDI.";
            nativeFileSyncStatus = NativeLeaderProgress(fileSession, "MIDI");
        }
        if (nativeLeaderTracks is { Complete: false, Failed: false } trackSession &&
            (now - trackSession.CreatedQpc) / (double)Stopwatch.Frequency > 25)
        {
            trackSession.Failed = true;
            trackSession.Failure = "No track READY from " + string.Join(", ",
                trackSession.Expected.Except(trackSession.Ready).Select(PartyMemberName)) + ".";
            nativeTracksSyncStatus = NativeLeaderProgress(trackSession, "Tracks");
        }
        if (nativeLeaderInstruments is { Complete: false, Failed: false } instrumentSession &&
            (now - instrumentSession.CreatedQpc) / (double)Stopwatch.Frequency > 35)
        {
            instrumentSession.Failed = true;
            var missed = instrumentSession.Expected.Except(instrumentSession.Ready)
                .Select(PartyMemberName);
            instrumentSession.Failure = "No instrument READY from " + string.Join(", ", missed) + ".";
            nativeInstrumentSyncStatus = NativeLeaderProgress(instrumentSession, "Instruments");
        }
    }

    private void FailFollowerFile(NativeFollowerFile file, string code, string detail)
    {
        file.Failed = true;
        nativeFollowerFile = null;
        nativeFileSyncStatus = "Conductor MIDI cannot be loaded: " + detail;
        plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeFileFailed(file.Cue.Nonce, code), out _);
        // Keep expectedPartyMidiHash set: auto-accept must not play an old, wrong song.
    }

    /// <summary>Block native START or auto-confirm when the current selection
    /// is unresolved or the conductor's SET hasn't completed.</summary>
    private bool NativeLeaderSyncIsReady(out string reason)
    {
        reason = "";
        if (!NativeSyncEnabled) return true;
        if (plugin.Instruments.InProgress)
        { reason = "The FFXIV instrument is still switching."; return false; }
        if (nativeFollowerFile is not null)
        { reason = "Waiting to load the conductor's exact MIDI file."; return false; }
        if (nativeFollowerInstrument is not null)
        { reason = "Waiting to equip the conductor's chosen instrument."; return false; }
        if (nativeFollowerTracks is not null)
        { reason = "Applying conductor party-track map."; return false; }
        if (nativeTracksSyncStatus.StartsWith("SET Party Tracks rejected", StringComparison.Ordinal))
        { reason = nativeTracksSyncStatus; return false; }
        if (pendingNativeUnequip)
        { reason = "Leader STOP requested; unequipping Performance."; return false; }
        if (cfg.EnsembleMode == (int)EnsembleRole.Follower &&
            !string.IsNullOrEmpty(expectedPartyMidiHash) &&
            !string.Equals(nativeLoadedSha, expectedPartyMidiHash, StringComparison.OrdinalIgnoreCase))
        { reason = "Wrong MIDI loaded compared with conductor (SHA-256 mismatch)."; return false; }
        if (cfg.EnsembleMode == (int)EnsembleRole.Conductor)
        {
            if (!string.IsNullOrEmpty(nativeLastFileSendError))
            { reason = nativeLastFileSendError; return false; }
            if (nativeLeaderFile is { Complete: false } file)
            { reason = NativeLeaderProgress(file, "MIDI"); return false; }
            if (nativeLeaderInstruments is { Complete: false } instruments)
            { reason = NativeLeaderProgress(instruments, "Instruments"); return false; }
            if (nativeLeaderTracks is { Complete: false } tracks)
            { reason = NativeLeaderProgress(tracks, "Tracks"); return false; }
            if (nativeLeaderInstruments is { Complete: true } good)
            {
                ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
                int required = good.Instruments.GetValueOrDefault(me);
                if (required != 0 && !plugin.Instruments.IsReadyFor(required))
                { reason = "Conductor's own instrument is not ready."; return false; }
            }
        }
        return true;
    }

    private void CancelNativeLeaderSync(string message)
    {
        nativeLeaderFile = null;
        nativeLeaderInstruments = null;
        nativeLeaderTracks = null;
        nativeFollowerTracks = null;
        nativeFollowerFile = null;
        nativeFollowerInstrument = null;
        expectedPartyMidiHash = "";
        nativeLastFileSendError = "";
        nativeFileSyncStatus = message;
        nativeInstrumentSyncStatus = message;
        nativeTracksSyncStatus = message;
    }
}
