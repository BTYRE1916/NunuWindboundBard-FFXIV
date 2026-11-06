using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private string oneClickStatus = "One-click party ensemble is off.";
    private readonly HashSet<string> receivedOneClickNonces = new(StringComparer.Ordinal);
    private long lastOneClickCueQpc;

    private void DrawOneClickControls(EnsembleRole role)
    {
        ImGui.TextColored(new Vector4(.48f, .95f, .78f, 1), Loc.T("ONE-CLICK PARTY ENSEMBLE — NO MANUAL READY CHECK"));
        ImGui.TextWrapped(Loc.T("One click automatically prepares the selected instruments, waits for " +
            "party-member READY replies, then sends a common START cue. Followers load " +
            "the matching MIDI, select assigned parts and play without per-song confirmations. " +
            "No VPN, port forwarding, Windows IPC, or game Ready Check required."));
        ImGui.TextDisabled(Loc.T("Both computers must have correctly synchronised system clocks (NTP). " +
            "Party-chat delivery and client frame timing can cause drift; native FFXIV Ensemble remains more precise."));
        if (role == EnsembleRole.Follower)
        {
            bool allow = cfg.OneClickAllowLeaderStart;
            if (ImGui.Checkbox(Loc.T("Allow my FFXIV party leader to start my Bard automatically"), ref allow))
            {
                cfg.OneClickAllowLeaderStart = allow;
                cfg.Save();
            }
            bool follow = cfg.OneClickFollowTrackAssignments;
            if (ImGui.Checkbox(Loc.T("Use conductor's party-member track assignments"), ref follow))
            {
                cfg.OneClickFollowTrackAssignments = follow;
                cfg.Save();
                if (!plugin.Player.IsPlaying) ApplyPartyAssignmentsToLocal();
            }
            ImGui.TextDisabled(Loc.T("Enable permission and ARM output once per session. " +
                "The conductor then selects and equips your instrument automatically."));
        }
        else if (role == EnsembleRole.Conductor)
        {
            DrawConductorInstrumentPlan();
            if (ImGui.Button(Loc.T("PLAY ENSEMBLE — auto-switch instruments & start"))) CueOneClickEnsemble();
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("STOP ensemble"))) StopPlayback();
            ImGui.TextDisabled(Loc.T("Leader only. One click: PREPARE instruments → wait for READY → START together. " +
                "Followers only opt in once per plugin session; no per-song acceptance required."));
        }
        else ImGui.TextDisabled(Loc.T("Choose Conductor or Follower first."));
        ImGui.TextWrapped(Loc.T(oneClickStatus));
        if (ImGui.SmallButton(Loc.T("Copy one-click diagnostics")))
            ImGui.SetClipboardText($"Role: {(EnsembleRole)cfg.EnsembleMode}\n" +
                $"Cross-PC enabled: {cfg.GameEnsembleEnabled}; One-click: {cfg.GameEnsembleOneClick}\n" +
                $"Follower opt-in: {cfg.OneClickAllowLeaderStart}; Follow parts: {cfg.OneClickFollowTrackAssignments}\n" +
                $"Armed: {outputArmed}; Performance ready: {plugin.IsGameOutputSafe()}\n" +
                $"MIDI: {Path.GetFileName(loaded)}; Playlist index: {activePlaylist + 1}\n" +
                $"Selected lanes: {lanes.Count(x => x.Include)}; Roster map: {partyAssignments.Count}\n" +
                $"Clock UTC: {DateTimeOffset.UtcNow:O}\n" +
                $"Party chat: {plugin.PartyChat.Status}\nInstrument plan: {partyInstrumentStatus}\n" +
                $"Native instrument: {plugin.Instruments.Status}\nOne-click status: {oneClickStatus}\n");
        ImGui.TextDisabled(Loc.T("PREPARE, READY and PLAY are visible FFXIV party messages. " +
            "No file contents, filesystem paths or account IDs are transmitted."));
    }

    /// <summary>One user click launches automatic party instrument preparation.
    /// The actual START is sent only after every assigned Bard acknowledges READY.</summary>
    private void CueOneClickEnsemble()
    {
        try
        {
            if (!cfg.GameEnsembleEnabled || !cfg.GameEnsembleOneClick ||
                (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor)
                throw new InvalidOperationException("Select Cross-PC / One-click Conductor first.");
            if (!cfg.GamePartyChatEnabled)
                throw new InvalidOperationException("Enable FFXIV party-chat coordination.");
            if (preparingConductor is not null)
                throw new InvalidOperationException("Already preparing ensemble instruments. Click STOP to cancel.");
            if (lastOneClickCueQpc != 0 && Stopwatch.GetTimestamp() - lastOneClickCueQpc < 2 * Stopwatch.Frequency)
                throw new InvalidOperationException("A conductor cue was just sent; avoid repeated clicks.");
            if (string.IsNullOrWhiteSpace(loaded) || song is null || arranged is null ||
                activePlaylist < 0 || activePlaylist >= cfg.Playlist.Count)
                throw new InvalidOperationException("Select a MIDI from the Ensemble playlist first.");
            if (!string.Equals(Path.GetFullPath(loaded), Path.GetFullPath(cfg.Playlist[activePlaylist]),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Current MIDI isn't selected in the Ensemble playlist.");
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                songFlow.HasPending || pendingInstrumentSong is not null)
                throw new InvalidOperationException("STOP/reset previous playback before starting another ensemble.");
            var members = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party).Select(p => p.ContentId).ToList();
            if (members.Count < 2)
                throw new InvalidOperationException("Join an FFXIV party with the other Bards first.");
            string packedParts = OneClickEnsembleCue.EncodeAssignments(
                lanes.Select(l => l.Key).ToList(), partyAssignments, members, out string trackRosterTag);
            if (packedParts != "-") ApplyPartyAssignmentsToLocal();
            string hash = GamePartyCueProtocol.GetHash(loaded);
            BeginOneClickInstrumentPreparation(activePlaylist, hash, trackRosterTag, packedParts, members);
        }
        catch (Exception ex)
        {
            if (preparingConductor is not null)
                AbortOneClickConductor("One-click conductor failed: " + ex.Message);
            else
            {
                oneClickStatus = "One-click conductor failed: " + ex.Message;
                ensembleStatus = oneClickStatus;
            }
        }
    }

    /// <summary>Invoked only after the existing party-leader name verification.
    /// The performer must have explicitly allowed remote START for this session.</summary>
    private void ReceiveOneClickEnsemble(GamePartyCue cue)
    {
        if (!cfg.GameEnsembleEnabled || !cfg.GameEnsembleOneClick ||
            !cfg.GamePartyChatEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower)
            return;
        if (!cfg.OneClickAllowLeaderStart)
        {
            oneClickStatus = "Ignored conductor START: permission not enabled on this Follower.";
            return;
        }
        if (preparingFollower is not { ReadyAckSent: true } session ||
            session.Nonce != cue.Nonce || !session.Hash.Equals(cue.Hash, StringComparison.OrdinalIgnoreCase) ||
            (Stopwatch.GetTimestamp() - session.CreatedQpc) / (double)Stopwatch.Frequency > 65 ||
            plugin.Instruments.InProgress ||
            (session.InstrumentId != 0 && !plugin.Instruments.IsReadyFor(session.InstrumentId)))
        {
            oneClickStatus = "Ignored START: conductor instrument PREPARE not completed. " +
                "Check party instrument status and press PLAY again.";
            return;
        }
        if (!receivedOneClickNonces.Add(cue.Nonce))
        {
            oneClickStatus = "Ignored repeated one-click START cue (duplicate nonce).";
            return;
        }
        // Bound the in-memory duplicate cache; it is not a persistent credential.
        if (receivedOneClickNonces.Count > 128) receivedOneClickNonces.Clear();
        try
        {
            if (!OneClickEnsembleCue.TryGetLocalStartQpc(cue.StartUnixMs,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Stopwatch.GetTimestamp(), Stopwatch.Frequency,
                out long startQpc, out string clockStatus))
                throw new InvalidOperationException(clockStatus);
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                songFlow.HasPending || pendingInstrumentSong is not null)
                throw new InvalidOperationException("Finish the previous performance and reset the MIDI before the next cue.");

            // If a follower hasn't selected the song yet, the ONE cue may load
            // the matching index from its saved Ensemble playlist. No MIDI files
            // or OS paths are transferred over FFXIV party chat.
            bool matching = !string.IsNullOrWhiteSpace(loaded) && File.Exists(loaded) &&
                GamePartyCueProtocol.GetHash(loaded).Equals(cue.Hash, StringComparison.OrdinalIgnoreCase);
            if (!matching)
            {
                if (cue.PlaylistIndex < 0 || cue.PlaylistIndex >= cfg.Playlist.Count)
                    throw new InvalidOperationException("Matching MIDI isn't loaded; the leader's playlist entry is missing locally.");
                string path = cfg.Playlist[cue.PlaylistIndex];
                if (!File.Exists(path) ||
                    !GamePartyCueProtocol.GetHash(path).Equals(cue.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The Ensemble playlist entry differs from the conductor's MIDI. " +
                        "Load exactly the same file or match playlist order.");
                if (!Load(path)) throw new InvalidOperationException("Could not load the matching Ensemble MIDI: " + status);
            }
            if (song is null || arranged is null) throw new InvalidOperationException("MIDI is not prepared.");

            if (cue.PackedAssignments != "-" && cfg.OneClickFollowTrackAssignments)
            {
                var members = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party).Select(p => p.ContentId).ToList();
                if (!OneClickEnsembleCue.TryDecodeAssignments(cue.PackedAssignments,
                    cue.RosterTag, lanes.Select(x => x.Key).ToList(), members,
                    out var mapping, out string error))
                    throw new InvalidOperationException("Conductor track map rejected: " + error);
                partyAssignments.Clear();
                foreach (var (key, id) in mapping) partyAssignments[key] = id;
                partyAssignmentsReceivedForCurrentMidi = true;
                ApplyPartyAssignmentsToLocal();
            }
            expectedPartyMidiHash = cue.Hash;
            if (Math.Abs(cfg.PlaybackSpeed * 1000 - cue.SpeedThousand) > 0.5f)
            {
                cfg.PlaybackSpeed = cue.SpeedThousand / 1000f;
                cfg.Save();
            }
            var readiness = GetGameEnsembleReadiness(out string why);
            if (readiness is not (GameEnsembleReadiness.Ready or GameEnsembleReadiness.Silent))
                throw new InvalidOperationException("This Bard isn't ready for the conductor cue: " + why);
            if (readiness == GameEnsembleReadiness.Silent)
            {
                oneClickStatus = "Conductor cue received. No tracks assigned to my character; staying silent.";
                ensembleStatus = oneClickStatus;
                return;
            }
            // Recalculate target after MIDI load and arrangement work; refusing a
            // late cue is better than dumping thousands of events in one frame.
            if (!OneClickEnsembleCue.TryGetLocalStartQpc(cue.StartUnixMs,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Stopwatch.GetTimestamp(), Stopwatch.Frequency,
                out startQpc, out clockStatus))
                throw new InvalidOperationException(clockStatus);
            StartSongCore(startQpc);
            oneClickStatus = "Received conductor START. " + clockStatus +
                $" Playing {lanes.Count(x => x.Include)} assigned MIDI lane(s).";
            ensembleStatus = oneClickStatus;
            preparingFollower = null;
        }
        catch (Exception ex)
        {
            oneClickStatus = "Conductor START rejected: " + ex.Message;
            ensembleStatus = oneClickStatus;
        }
    }
}
