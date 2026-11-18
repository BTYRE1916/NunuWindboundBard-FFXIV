using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private double hostedTakeEndsMs;
    private bool HostedPlaybackBusy => plugin.Player.IsPlaying || plugin.Player.IsStopPending ||
        plugin.Player.IsRewindPending || MultiDeviceEnsemble.NowMs() < hostedTakeEndsMs;
    private int relaySetupConnection;
    private long relaySetupSequence;
    private int? relayInstrumentOverride;
    private string relaySongName = "";
    private string relaySetupFeedback = "";

    private void ClearHostedSetup()
    {
        hostedTakeEndsMs = 0;
        waitingForEnsembleCue = false;
        relaySetupConnection = 0;
        relaySetupSequence = 0;
        relayInstrumentOverride = null;
        relaySetupFeedback = "";
        relaySongName = "";
        networkStartFeedback = "";
    }

    private void DrawHostedEnsemble(EnsembleRole role)
    {
        if (role == EnsembleRole.Solo) return;
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.98f, .60f, .84f, 1f), Loc.T("CONCERT DIRECTOR — HOSTED BAND"));
        if (role == EnsembleRole.Follower)
        {
            int connection = plugin.Network.RelayFollowerConnectionId;
            ImGui.BeginDisabled(connection == 0);
            bool allow = connection != 0 && relaySetupConnection == connection;
            if (ImGui.Checkbox(Loc.T("Allow this conductor to change my instrument and arm me for cues"), ref allow))
            {
                relaySetupConnection = allow ? connection : 0;
                if (!allow) relayInstrumentOverride = null;
                relaySetupFeedback = "";
            }
            ImGui.EndDisabled();
            DrawHostedLocalArm();
            ImGui.Checkbox(Loc.T("Wait for conductor cue"), ref waitingForEnsembleCue);
            DrawPartyAssignmentControls(role);
            if (connection != 0)
            {
                FollowerReady(out string reason);
                ImGui.TextWrapped(Loc.T(reason));
            }
            if (relaySetupFeedback.Length > 0) ImGui.TextWrapped(Loc.T(relaySetupFeedback));
            if (networkStartFeedback.Length > 0) ImGui.TextWrapped(Loc.T(networkStartFeedback));
            return;
        }

        bool busy = HostedPlaybackBusy;
        ImGui.BeginDisabled(busy);
        ImGui.SetNextItemWidth(Math.Min(350, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo(Loc.T("Ensemble MIDI"), song is null ? Loc.T("Choose a song") : Path.GetFileName(loaded)))
        {
            for (int i = 0; i < cfg.Playlist.Count; i++)
            {
                string path = cfg.Playlist[i];
                if (ImGui.Selectable($"{Path.GetFileName(path)}##hostedSong{i}", i == activePlaylist))
                    SelectPlaylistSong(path);
            }
            ImGui.EndCombo();
        }
        if (ImGui.SmallButton(Loc.T("Browse MIDI##hostedEnsemble"))) BrowseMidiToLoad();
        ImGui.EndDisabled();

        DrawHostedLocalArm();
        bool hosting = plugin.Network.IsHosting;
        ImGui.BeginDisabled(!hosting || song is null || busy);
        if (ImGui.Button(Loc.T("Prepare entire band"))) RequestHostedSetup(BandSetupAction.Prepare);
        if (ImGui.Button(Loc.T("Sync MIDI + track assignments"))) TryShareNetworkSession(resend: true);
        if (ImGui.Button(Loc.T("Set band instruments"))) RequestHostedSetup(BandSetupAction.SetInstruments);
        if (ImGui.Button(Loc.T("ARM all bards"))) RequestHostedSetup(BandSetupAction.Arm);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("DISARM all bards"))) RequestHostedSetup(BandSetupAction.Disarm);
        if (ImGui.Button(Loc.T("Start ensemble##hostedStart"))) CueEnsemble();
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!hosting);
        if (ImGui.Button(Loc.T("Stop connected bards"))) StopPlayback();
        ImGui.BeginDisabled(plugin.Network.SessionRevision <= 0);
        if (ImGui.Button(Loc.T("Stop ensemble + unequip band")))
        {
            StopPlayback();
            try
            {
                plugin.Network.SendBandSetup(BandSetupAction.Unequip, new Dictionary<ulong, int>());
                ApplyHostedSetup(BandSetupAction.Unequip, 0, follower: false);
            }
            catch (Exception ex) { ensembleStatus = "Band unequip failed: " + ex.Message; }
        }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (!hosting) ImGui.TextDisabled(Loc.T("Choose Host Band above to enable band commands."));
        if (hosting)
        {
            ImGui.TextWrapped(Loc.T(ensembleStatus));
            DrawNetworkPerformers();
        }
        ImGui.BeginDisabled(busy);
        if (ImGui.CollapsingHeader(Loc.T("Band track assignments##hostedTracks"), ImGuiTreeNodeFlags.DefaultOpen))
            DrawPartyAssignmentControls(role);
        if (ImGui.CollapsingHeader(Loc.T("Band instruments##hostedInstruments"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (song is null) ImGui.TextDisabled(Loc.T("Choose an Ensemble MIDI to set instruments."));
            else DrawConductorInstrumentPlan(hosted: true);
        }
        ImGui.EndDisabled();
    }

    private void DrawHostedLocalArm()
    {
        bool armed = outputArmed;
        if (ImGui.Checkbox(Loc.T("ARM output on this Bard"), ref armed)) HudSetOutputArmed(armed);
    }

    private void RequestHostedSetup(BandSetupAction action)
    {
        if (!IsRelayBandEnabled || !plugin.Network.IsHosting || cfg.EnsembleMode != (int)EnsembleRole.Conductor)
            return;
        if (HostedPlaybackBusy)
        {
            ensembleStatus = "Stop the ensemble before changing band setup.";
            return;
        }
        if (!TryShareNetworkSession()) return;
        try
        {
            var instruments = GetPartyInstrumentChoices();
            ulong local = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
            ApplyHostedSetup(action, instruments.GetValueOrDefault(local), follower: false);
            int sent = plugin.Network.SendBandSetup(action, instruments);
            ensembleStatus = $"Band setup ({action}) sent to {sent} connected follower(s).";
        }
        catch (Exception ex) { ensembleStatus = "Band setup failed: " + ex.Message; }
    }

    private void ReceiveHostedSetup(NetworkNotice notice)
    {
        int connection = plugin.Network.RelayFollowerConnectionId;
        if (!IsRelayBandEnabled || cfg.EnsembleMode != (int)EnsembleRole.Follower || connection == 0 ||
            connection != notice.ConnectionId || networkRevision != notice.Revision ||
            !string.Equals(networkSongHash, notice.Hash, StringComparison.OrdinalIgnoreCase) ||
            notice.SetupSequence <= relaySetupSequence) return;
        relaySetupSequence = notice.SetupSequence;
        if (!partyAssignmentsReceivedForCurrentMidi || song is null || arranged is null ||
            currentSongHash != BitConverter.ToInt64(Convert.FromHexString(notice.Hash), 0))
        {
            relaySetupFeedback = "Band setup needs the current shared MIDI and track assignments.";
            return;
        }
        if (relaySetupConnection != connection)
        {
            relaySetupFeedback = "Remote setup is off. Prepare this Bard locally or allow the conductor and request setup again.";
            return;
        }
        try
        {
            ApplyHostedSetup(notice.Setup, notice.Instrument, follower: true);
            relaySetupFeedback = $"Processed conductor setup ({notice.Setup}).";
        }
        catch (Exception ex) { relaySetupFeedback = "Band setup could not finish: " + ex.Message; }
    }

    private void ApplyHostedSetup(BandSetupAction action, int instrument, bool follower)
    {
        if (action == BandSetupAction.Unequip)
        {
            if (follower) waitingForEnsembleCue = false;
            pendingNativeUnequip = true; // Existing framework handler waits for note releases.
            return;
        }
        if (song is null || arranged is null || plugin.Player.IsPlaying || plugin.Player.IsStopPending ||
            plugin.Player.IsRewindPending || songFlow.HasPending || pendingInstrumentSong is not null)
            throw new InvalidOperationException("Load the shared MIDI and finish playback before preparing this Bard.");
        bool hasPart = arranged.Notes.Count > 0;
        if (action is BandSetupAction.SetInstruments or BandSetupAction.Prepare)
        {
            if (!InstrumentCatalog.Valid(instrument)) throw new InvalidDataException("Unknown band instrument.");
            relayInstrumentOverride = instrument;
            if (hasPart && instrument != 0 && !plugin.Instruments.IsReadyFor(instrument))
            {
                if (plugin.Instruments.InProgress && plugin.Instruments.TargetId != instrument)
                    throw new InvalidOperationException("Another instrument change is in progress.");
                if (!plugin.Instruments.InProgress) plugin.Instruments.Request(instrument);
            }
        }
        if (action is BandSetupAction.Arm or BandSetupAction.Disarm or BandSetupAction.Prepare)
        {
            bool arm = action != BandSetupAction.Disarm;
            HudSetOutputArmed(arm && hasPart);
            if (follower) waitingForEnsembleCue = arm;
        }
    }
}
