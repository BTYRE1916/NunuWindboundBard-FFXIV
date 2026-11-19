using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private string relayBandCode = "";
    private bool IsRelayBandEnabled => cfg.MultiDeviceEnabled && cfg.MultiDeviceUseRelay && !cfg.GameEnsembleEnabled;

    private IReadOnlyList<PartyChoice> GetConnectedBandChoices()
    {
        ulong local = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        var choices = new List<PartyChoice>();
        if (local != 0)
            choices.Add(new PartyChoice(local, Plugin.Objects.LocalPlayer?.Name.TextValue ?? "Conductor", true));
        foreach (var performer in plugin.Network.Performers)
            if (performer.ContentId != local)
                choices.Add(new PartyChoice(performer.ContentId, performer.Character, false));
        return choices;
    }

    private void ResetNetworkConnection()
    {
        if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
        plugin.Network.Stop();
        ClearHostedSetup();
        waitingForEnsembleCue = false;
        networkRevision = 0;
        networkSongHash = "";
        networkArrangement = null;
        partyAssignmentsReceivedForCurrentMidi = false;
    }

    private void DrawRelayBandControls(EnsembleRole role)
    {
        if (plugin.Network.IsJoining)
        {
            if (ImGui.Button(Loc.T("Cancel connection"))) ResetNetworkConnection();
            return;
        }
        if (role == EnsembleRole.Conductor)
        {
            if (plugin.Network.IsHosting)
            {
                if (ImGui.Button(Loc.T("Copy band join code")))
                    ImGui.SetClipboardText(plugin.Network.RelayJoinCode);
                ImGui.SameLine();
                if (ImGui.Button(Loc.T("Close band")))
                {
                    StopPlayback();
                    ResetNetworkConnection();
                    networkStatus = "Band closed.";
                    return;
                }
                return;
            }
            var address = cfg.MultiDeviceRelayAddress ?? "";
            ImGui.SetNextItemWidth(Math.Min(380, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputText(Loc.T("Band relay address"), ref address, 320))
            {
                cfg.MultiDeviceRelayAddress = address;
                cfg.Save();
            }
            ImGui.TextWrapped(Loc.T("Enter your hosted relay's wss:// address. Followers only need the join code."));
            if (ImGui.Button(Loc.T("Host Band")))
            {
                try
                {
                    ClearHostedSetup();
                    plugin.Network.HostBand(address, cfg.EnsembleGroup);
                    networkStatus = "";
                }
                catch (Exception error) { networkStatus = error.Message; }
            }
            return;
        }
        if (role != EnsembleRole.Follower)
        {
            ImGui.TextDisabled(Loc.T("Choose Conductor to host or Follower to join."));
            return;
        }
        if (plugin.Network.IsConnected)
        {
            ImGui.TextDisabled(Loc.T($"Clock synchronisation: {(plugin.Network.HasClockSync ? "READY" : "waiting for samples")} / {plugin.Network.RoundTripMs:0}ms RTT / {plugin.Network.ClockSpreadMs:0.0}ms clock spread"));
            int trim = Math.Clamp(cfg.RelayFollowerTimingTrimMs, -250, 250);
            ImGui.SetNextItemWidth(Math.Min(335, ImGui.GetContentRegionAvail().X));
            if (ImGui.SliderInt(Loc.T("Follower timing trim (ms)"), ref trim, -250, 250))
            {
                cfg.RelayFollowerTimingTrimMs = trim;
                cfg.Save();
            }
            ImGui.TextDisabled(Loc.T("Negative = your Bard earlier; positive = later. Start at 0, adjust by 20ms per duet test."));
            if (ImGui.Button(Loc.T("Leave band")))
            {
                ResetNetworkConnection();
                networkStatus = "Left the band.";
            }
            return;
        }
        ImGui.SetNextItemWidth(Math.Min(380, ImGui.GetContentRegionAvail().X));
        ImGui.InputText(Loc.T("Band join code"), ref relayBandCode, 2048, ImGuiInputTextFlags.Password);
        if (ImGui.Button(Loc.T("Join with Code")))
        {
            try
            {
                var local = Plugin.Objects.LocalPlayer;
                ulong contentId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
                ClearHostedSetup();
                plugin.Network.JoinBand(relayBandCode, local?.Name.TextValue ?? "", contentId);
                networkStatus = "";
            }
            catch (Exception error) { networkStatus = error.Message; }
        }
    }

    private void DrawRelayDiagnostics()
    {
        if (!ImGui.CollapsingHeader(Loc.T("Relay diagnostics"))) return;
        string report = plugin.Network.RelayReport;
        if (ImGui.Button(Loc.T("Copy relay report"))) ImGui.SetClipboardText(report);
        ImGui.TextDisabled(Loc.T("Recent connections in this plugin session (UTC)."));
        ImGui.BeginChild("##relayHistory", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 10));
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(report);
        ImGui.PopTextWrapPos();
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY()) ImGui.SetScrollHereY(1f);
        ImGui.EndChild();
    }

    private void DrawNetworkPerformers()
    {
        ImGui.TextUnformatted(Loc.T("Connected Windbound performers:"));
        var performers = plugin.Network.Performers;
        if (performers.Count == 0) ImGui.TextDisabled(Loc.T("No followers connected yet."));
        foreach (var peer in performers)
        {
            var color = peer.Ready ? new Vector4(.45f, .96f, .58f, 1f) : new Vector4(1f, .77f, .38f, 1f);
            string displayState = peer.Ready ? "READY" :
                IsRelayBandEnabled && peer.Detail.StartsWith("CUED / PLAYING:", StringComparison.Ordinal)
                    ? "CUED / PLAYING" : "WAITING";
            ImGui.TextColored(color, Loc.T($"{peer.Character} — {displayState} (rev {peer.Revision})"));
            if (!peer.Ready || IsRelayBandEnabled) ImGui.TextWrapped(Loc.T(peer.Detail));
            if (IsRelayBandEnabled && peer.StartSequence > 0)
            {
                ImGui.TextWrapped($"START #{peer.StartSequence}: {peer.StartOutcome}");
                ImGui.TextWrapped($"Timing: {peer.TimingOutcome}");
            }
            if (IsRelayBandEnabled && peer.SupportsBandSetup && !peer.Ready && !HostedPlaybackBusy &&
                plugin.Network.SessionRevision > 0 && ImGui.SmallButton(Loc.T("Retry MIDI + tracks##relayRetry") + peer.ContentId))
                networkStatus = plugin.Network.RetryBandMember(peer.ContentId) ? "Sent the current MIDI and parts to " + peer.Character : "Performer disconnected.";
            if (IsRelayBandEnabled && !peer.SupportsBandSetup)
                ImGui.TextDisabled(Loc.T("This follower needs v0.46.3 for conductor instrument and ARM commands."));
        }
    }

    private void DrawNetworkStatus()
    {
        ImGui.TextWrapped(Loc.T(plugin.Network.Status));
        if (!string.IsNullOrEmpty(networkStatus)) ImGui.TextWrapped(Loc.T(networkStatus));
    }
}
