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
/// One-click conductor instrument preparation. Every participating follower
/// must have explicitly enabled the already-existing OneClickAllowLeaderStart
/// setting for this plugin session. Once enabled, there are NO per-song clicks.
/// Two phases: PREPARE -> READY acknowledgements -> scheduled START.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class ConductorInstrumentPreparation
    {
        public required string Nonce;
        public required string Hash;
        public required string RosterTag;
        public required string TrackRosterTag;
        public required string PackedParts;
        public required int PlaylistIndex;
        public required Dictionary<ulong, int> Instruments;
        public required HashSet<ulong> ExpectedFollowers;
        public HashSet<ulong> ReadyFollowers { get; } = new();
        public required ulong LocalId;
        public required bool LocalSilent;
        public long CreatedQpc;
    }

    private sealed class IncomingInstrumentPreparation
    {
        public required string Nonce;
        public required string Hash;
        public int InstrumentId;
        public long CreatedQpc;
        public long LastAckAttemptQpc;
        public bool ReadyAckSent;
        public bool FailureReported;
    }

    private ConductorInstrumentPreparation? preparingConductor;
    private IncomingInstrumentPreparation? preparingFollower;
    private string partyInstrumentStatus = "Choose instruments for the party before starting an ensemble.";

    private Dictionary<ulong, int> GetPartyInstrumentChoices()
    {
        var selected = new Dictionary<ulong, int>();
        // Infer for all assigned members on every draw/SET. This automatically
        // refreshes when the conductor changes track assignments, without
        // permanently writing suggestions into manual configuration.
        foreach (var item in GetMidiPartySuggestions())
            if (item.InstrumentId != 0) selected[item.ContentId] = item.InstrumentId;
        if (!string.IsNullOrWhiteSpace(loaded) &&
            cfg.PartyInstrumentsByFile.TryGetValue(loaded, out var stored))
        {
            foreach (var (id, instrument) in stored)
                if (ulong.TryParse(id, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong contentId) &&
                    InstrumentCatalog.Valid(instrument)) selected[contentId] = instrument;
        }
        // The conductor's existing per-MIDI selection remains the default for
        // their own Bard; party-specific choices override it.
        ulong local = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (local != 0 && cfg.InstrumentsByFile.TryGetValue(loaded, out int explicitLocal) &&
            InstrumentCatalog.Valid(explicitLocal) &&
            !(cfg.PartyInstrumentsByFile.TryGetValue(loaded, out var manual) && manual.ContainsKey(local.ToString("X16"))))
            selected[local] = explicitLocal;
        else if (local != 0 && !selected.ContainsKey(local)) selected[local] = GetInstrumentChoice();
        return selected;
    }

    private void SetPartyInstrumentChoice(ulong contentId, int instrument)
    {
        if (contentId == 0 || !InstrumentCatalog.Valid(instrument) ||
            string.IsNullOrWhiteSpace(loaded) || preparingConductor is not null) return;
        // Persist ONLY the conductor's explicit manual choice, not the inferred
        // values of every other Bard. That way replacing/assigning MIDI tracks
        // automatically refreshes suggestions for those other party members.
        if (!cfg.PartyInstrumentsByFile.TryGetValue(loaded, out var chosen))
            cfg.PartyInstrumentsByFile[loaded] = chosen = new(StringComparer.OrdinalIgnoreCase);
        chosen[contentId.ToString("X16")] = instrument;
        cfg.Save();
    }

    private void DrawConductorInstrumentPlan()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.99f, .58f, .84f, 1f), Loc.T("PARTY INSTRUMENTS — CONDUCTOR CHOICE"));
        ImGui.TextWrapped(Loc.T(cfg.GameEnsembleOneClick
            ? "Choose a party instrument per Bard. The legacy one-click mode will prepare everyone."
            : "Choose the instrument for each Bard, then press SET PARTY INSTRUMENTS. " +
              "Followers switch automatically, then the game synchronises START via native Ready Check."));
        var selected = GetPartyInstrumentChoices();
        var members = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        if (cfg.AutoDetectMidiTrackInstruments && partyAssignments.Count == 0)
            ImGui.TextDisabled(Loc.T("Assign MIDI tracks to party members first to infer their instruments."));
        ImGui.BeginChild("##ConductorInstrumentRoster", new Vector2(0,
            Math.Min(290f, Math.Max(85f, 42f * members.Count))), true);
        foreach (var member in members)
        {
            int value = selected.GetValueOrDefault(member.ContentId);
            string contentIdKey = member.ContentId.ToString("X16");
            bool manuallyAssigned = !string.IsNullOrWhiteSpace(loaded) &&
                ((cfg.PartyInstrumentsByFile.TryGetValue(loaded, out var overrides) &&
                  overrides.ContainsKey(contentIdKey)) ||
                 (member.IsLocal && cfg.InstrumentsByFile.ContainsKey(loaded)));
            ImGui.TextUnformatted(Loc.T((member.IsLocal ? "[Me] " : "") + member.Name +
                (manuallyAssigned ? " [manual]" : value > 0 ? " [MIDI]" : " [unassigned]")));
            ImGui.SameLine(Math.Max(190f, ImGui.GetContentRegionAvail().X * .46f));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##InstFor" + member.ContentId, Loc.T(InstrumentCatalog.Display(value))))
            {
                if (ImGui.Selectable(Loc.T("Keep current##0-" + member.ContentId), value == 0))
                    SetPartyInstrumentChoice(member.ContentId, 0);
                foreach (var item in InstrumentCatalog.All)
                    if (ImGui.Selectable(Loc.T(item.DisplayName + "##" + member.ContentId + "-" + item.Id),
                        value == item.Id)) SetPartyInstrumentChoice(member.ContentId, item.Id);
                ImGui.EndCombo();
            }
        }
        ImGui.EndChild();
        DrawMidiMetadataControls();
        ImGui.TextDisabled(Loc.T("Selections are saved per MIDI. 'Keep current' requires the Bard already in Performance."));
        ImGui.TextWrapped(Loc.T(partyInstrumentStatus));
    }

    /// <summary>Invoked from the one-click Play button; automatically prepares
    /// every assigned party member without separate follower interactions.</summary>
    private void BeginOneClickInstrumentPreparation(int playlistIndex, string hash,
        string trackRosterTag, string packedParts, IReadOnlyList<ulong> roster)
    {
        if (preparingConductor is not null)
            throw new InvalidOperationException("Instrument preparation is already running. Use STOP to cancel.");
        var chosen = GetPartyInstrumentChoices();
        string packed = PartyInstrumentPlan.Encode(chosen, roster, out string rosterTag);
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var map = new Dictionary<ulong, int>();
        if (!PartyInstrumentPlan.TryDecode(packed, rosterTag, roster, out map, out string error))
            throw new InvalidOperationException("Cannot prepare party instruments: " + error);
        ulong localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (localId == 0) throw new InvalidOperationException("Cannot identify conductor in party.");
        var players = partyAssignments.Values.Where(id => id != 0).Distinct().ToHashSet();
        if (players.Count == 0) players = roster.Where(id => id != 0).ToHashSet();
        if (arranged?.Notes.Count > 0 && !outputArmed)
            throw new InvalidOperationException("ARM output before starting the conductor's own MIDI parts.");
        if (arranged?.Notes.Count > 0 && map.GetValueOrDefault(localId) == 0 && !plugin.IsGameOutputSafe())
            throw new InvalidOperationException("Open your own Bard Performance first, or assign an instrument to the conductor.");
        var p = new ConductorInstrumentPreparation {
            Nonce = nonce, Hash = hash, RosterTag = rosterTag,
            TrackRosterTag = trackRosterTag, PackedParts = packedParts,
            PlaylistIndex = playlistIndex, Instruments = map,
            ExpectedFollowers = players.Where(id => id != localId).ToHashSet(),
            LocalId = localId, LocalSilent = arranged?.Notes.Count == 0,
            CreatedQpc = Stopwatch.GetTimestamp()
        };
        // Validate the eventual START before any PREPARE is transmitted.
        _ = GamePartyCueProtocol.Start(playlistIndex, loaded,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 8000,
            (int)Math.Round(cfg.PlaybackSpeed * 1000), trackRosterTag, packedParts, nonce);
        string command = GamePartyCueProtocol.Prepare(playlistIndex, loaded, nonce, rosterTag, packed);
        plugin.PartyChat.Enabled = true;
        if (!plugin.PartyChat.TryBroadcast(command, out string reason))
            throw new InvalidOperationException("Could not send instrument preparation: " + reason);
        preparingConductor = p;
        lastOneClickCueQpc = Stopwatch.GetTimestamp();
        int localInstrument = map.GetValueOrDefault(localId);
        if (!p.LocalSilent && localInstrument != 0 && !plugin.Instruments.IsReadyFor(localInstrument))
        {
            if (plugin.Instruments.InProgress)
            {
                if (plugin.Instruments.TargetId != localInstrument)
                    throw new InvalidOperationException("Different local instrument change is in progress; STOP and retry.");
            }
            else plugin.Instruments.Request(localInstrument);
        }
        partyInstrumentStatus = "PREPARE sent. Waiting for assigned Bards to switch instruments automatically...";
        oneClickStatus = partyInstrumentStatus;
        ensembleStatus = oneClickStatus;
    }

    private void ReceivePartyInstrumentPreparation(GamePartyCue cue)
    {
        if (!cfg.GameEnsembleEnabled || !cfg.GameEnsembleOneClick || !cfg.OneClickAllowLeaderStart ||
            !cfg.GamePartyChatEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) return;
        if (preparingFollower?.Nonce == cue.Nonce) return; // duplicate chat echo
        try
        {
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                songFlow.HasPending || pendingInstrumentSong is not null)
                throw new InvalidOperationException("STOP the previous performance before ensemble preparation.");
            var roster = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party).Select(m => m.ContentId).ToArray();
            if (!PartyInstrumentPlan.TryDecode(cue.PackedInstruments, cue.RosterTag, roster,
                out var instruments, out string error))
                throw new InvalidOperationException(error);
            ulong localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
            if (!instruments.TryGetValue(localId, out int instrument))
                throw new InvalidOperationException("My Bard is absent from the conductor's party map.");
            EnsureOneClickMidiLoaded(cue.PlaylistIndex, cue.Hash);
            // Don't overwrite the user's preferred per-song instrument. Conductor's
            // choice is a session-only override for this particular ensemble cue.
            preparingFollower = new IncomingInstrumentPreparation {
                Nonce = cue.Nonce, Hash = cue.Hash, InstrumentId = instrument,
                CreatedQpc = Stopwatch.GetTimestamp()
            };
            if (instrument != 0 && !plugin.Instruments.IsReadyFor(instrument))
            {
                if (plugin.Instruments.InProgress)
                {
                    if (plugin.Instruments.TargetId != instrument)
                        throw new InvalidOperationException("Another instrument switch is already active.");
                }
                else plugin.Instruments.Request(instrument);
            }
            partyInstrumentStatus = "Conductor chose " + InstrumentCatalog.Display(instrument) +
                ". Switching automatically; no follower confirmation required.";
            oneClickStatus = partyInstrumentStatus;
        }
        catch (Exception ex)
        {
            preparingFollower = null;
            partyInstrumentStatus = "Conductor instrument PREPARE failed: " + ex.Message;
            oneClickStatus = partyInstrumentStatus;
            // Convey a safe fixed status, not file paths or native exceptions.
            plugin.PartyChat.TryReply(GamePartyCueProtocol.Failed(cue.Nonce, "PREPARE"), out _);
        }
    }

    private void EnsureOneClickMidiLoaded(int playlistIndex, string hash)
    {
        bool match = !string.IsNullOrWhiteSpace(loaded) && File.Exists(loaded) &&
            GamePartyCueProtocol.GetHash(loaded).Equals(hash, StringComparison.OrdinalIgnoreCase);
        if (match) return;
        if (playlistIndex < 0 || playlistIndex >= cfg.Playlist.Count)
            throw new InvalidOperationException("Matching MIDI missing from Ensemble playlist.");
        string path = cfg.Playlist[playlistIndex];
        if (!File.Exists(path) || !GamePartyCueProtocol.GetHash(path).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Song hashes differ. Load the exact same MIDI as the conductor.");
        if (!Load(path)) throw new InvalidOperationException("Could not load selected ensemble MIDI.");
    }

    /// <summary>Called every game framework frame AFTER Instruments.Update and
    /// received party commands have been processed. No Draw/focus dependency.</summary>
    public void PollOneClickInstrumentPreparation()
    {
        if (!cfg.GameEnsembleEnabled || !cfg.GameEnsembleOneClick)
        {
            preparingFollower = null;
            preparingConductor = null;
            return;
        }
        long now = Stopwatch.GetTimestamp();
        if (preparingFollower is { } follower && !follower.ReadyAckSent && !follower.FailureReported)
        {
            double elapsed = (now - follower.CreatedQpc) / (double)Stopwatch.Frequency;
            if ((follower.InstrumentId != 0 && plugin.Instruments.HasFailed &&
                 !plugin.Instruments.IsReadyFor(follower.InstrumentId)) || elapsed > 35)
            {
                follower.FailureReported = true;
                partyInstrumentStatus = "Instrument preparation failed: " + plugin.Instruments.Status;
                plugin.PartyChat.TryReply(GamePartyCueProtocol.Failed(follower.Nonce,
                    follower.InstrumentId != 0 && plugin.Instruments.HasFailed ? "SWITCH" : "TIMEOUT"), out _);
            }
            else if (!plugin.Instruments.InProgress &&
                (follower.InstrumentId == 0 || plugin.Instruments.IsReadyFor(follower.InstrumentId)) &&
                plugin.IsGameOutputSafe() && outputArmed && song is not null && arranged is not null)
            {
                if (follower.LastAckAttemptQpc == 0 ||
                    (now - follower.LastAckAttemptQpc) >= Stopwatch.Frequency)
                {
                    follower.LastAckAttemptQpc = now;
                    if (plugin.PartyChat.TryReply(GamePartyCueProtocol.Ready(follower.Nonce,
                        follower.InstrumentId), out string detail))
                    {
                        follower.ReadyAckSent = true;
                        partyInstrumentStatus = "Instrument READY (" +
                            InstrumentCatalog.Display(follower.InstrumentId) + "); waiting for PLAY.";
                    }
                    else partyInstrumentStatus = "Could not send instrument READY: " + detail;
                }
            }
        }
        if (preparingConductor is not { } conductor) return;
        if ((now - conductor.CreatedQpc) / (double)Stopwatch.Frequency > 35)
        {
            var pending = conductor.ExpectedFollowers.Except(conductor.ReadyFollowers).ToList();
            string names = string.Join(", ", PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
                .Where(m => pending.Contains(m.ContentId)).Select(m => m.Name));
            partyInstrumentStatus = "Timed out waiting for instrument READY: " + names +
                ". Check follower opt-in, ARM and Performance, then press PLAY again.";
            oneClickStatus = partyInstrumentStatus;
            preparingConductor = null;
            plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.Stop, out _);
            return;
        }
        int localInstrumentChoice = conductor.Instruments.GetValueOrDefault(conductor.LocalId);
        if (!conductor.LocalSilent && localInstrumentChoice != 0 && plugin.Instruments.HasFailed &&
            !plugin.Instruments.IsReadyFor(localInstrumentChoice))
        {
            AbortOneClickConductor("Conductor instrument failed: " + plugin.Instruments.Status);
            return;
        }
        if (!conductor.ExpectedFollowers.SetEquals(conductor.ReadyFollowers))
        {
            partyInstrumentStatus = $"Preparing instruments: {conductor.ReadyFollowers.Count}/{conductor.ExpectedFollowers.Count} " +
                "followers READY.";
            oneClickStatus = partyInstrumentStatus;
            return;
        }
        int localInstrument = conductor.Instruments.GetValueOrDefault(conductor.LocalId);
        if (!conductor.LocalSilent && (!outputArmed || !plugin.IsGameOutputSafe() ||
            plugin.Instruments.InProgress ||
            (localInstrument != 0 && !plugin.Instruments.IsReadyFor(localInstrument))))
        {
            partyInstrumentStatus = "All followers READY; waiting for my instrument and ARM output.";
            return;
        }
        try { LaunchPreparedOneClickEnsemble(conductor); }
        catch (Exception ex) { AbortOneClickConductor("Automatic start failed: " + ex.Message); }
    }

    private void OnPartyInstrumentReply(GamePartyCue cue, ulong senderId)
    {
        if (preparingConductor is not { } p || cue.Nonce != p.Nonce ||
            !p.ExpectedFollowers.Contains(senderId)) return;
        if (cue.Kind == GamePartyCueKind.Failed)
        {
            string name = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
                .FirstOrDefault(x => x.ContentId == senderId)?.Name ?? "Party member";
            AbortOneClickConductor(name + " could not prepare their instrument (" +
                cue.PackedInstruments + "). Check their Windbound status.");
            return;
        }
        if (cue.Kind != GamePartyCueKind.Ready ||
            !int.TryParse(cue.PackedInstruments, out int reported) ||
            reported != p.Instruments.GetValueOrDefault(senderId)) return;
        p.ReadyFollowers.Add(senderId);
    }

    private void LaunchPreparedOneClickEnsemble(ConductorInstrumentPreparation p)
    {
        if (loaded.Length == 0 || p.Hash != GamePartyCueProtocol.GetHash(loaded))
            throw new InvalidOperationException("The selected MIDI changed during preparation.");
        if (arranged is null || plugin.Player.IsPlaying || plugin.Player.IsStopPending ||
            plugin.Player.IsRewindPending || songFlow.HasPending)
            throw new InvalidOperationException("The previous song is not fully stopped.");
        int delayMs = Math.Clamp(cfg.EnsembleLeadMilliseconds, 6000, 15000);
        long startUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delayMs;
        if (!OneClickEnsembleCue.TryGetLocalStartQpc(startUtc,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Stopwatch.GetTimestamp(), Stopwatch.Frequency,
            out long localStart, out string clockDetail))
            throw new InvalidOperationException(clockDetail);
        string command = GamePartyCueProtocol.Start(p.PlaylistIndex, loaded, startUtc,
            (int)Math.Round(cfg.PlaybackSpeed * 1000), p.TrackRosterTag, p.PackedParts, p.Nonce);
        if (!plugin.PartyChat.TryBroadcast(command, out string reason))
            throw new InvalidOperationException("Could not broadcast ensemble START: " + reason);
        preparingConductor = null;
        if (!p.LocalSilent)
        {
            try { StartSongCore(localStart); }
            catch
            {
                plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.Stop, out _);
                throw;
            }
        }
        partyInstrumentStatus = "All Bards READY. One-click ensemble START sent; instruments switched automatically.";
        oneClickStatus = partyInstrumentStatus + " " + clockDetail;
        ensembleStatus = oneClickStatus;
    }

    private void AbortOneClickConductor(string detail)
    {
        preparingConductor = null;
        partyInstrumentStatus = detail;
        oneClickStatus = detail;
        ensembleStatus = detail;
        plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.Stop, out _);
    }

    private void CancelOneClickInstrumentPreparation()
    {
        preparingConductor = null;
        preparingFollower = null;
        plugin.Instruments.Cancel();
    }

    /// <summary>Null = normal local song preference, 0 = conductor explicitly
    /// chose Keep current, positive = ensemble-owned instrument choice.</summary>
    private int? GetOneClickInstrumentOverride()
    {
        if (!cfg.GameEnsembleEnabled || !cfg.GameEnsembleOneClick) return null;
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor &&
            preparingConductor is { } p) return p.Instruments.GetValueOrDefault(p.LocalId);
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
            preparingFollower is { } f) return f.InstrumentId;
        return null;
    }
}
