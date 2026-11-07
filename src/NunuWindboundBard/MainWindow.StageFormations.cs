using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// v0.30 stage formations. FFXIV party chat carries a single destination per
/// member, and optional vnavmesh safely walks that character to its stage spot.
/// This subsystem never starts music, changes song/track, or modifies actor XYZ.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class StageCaptureSession
    {
        public string Nonce = "";
        public string Name = "";
        public uint Territory;
        public Vector3 Anchor;
        public float Heading;
        public long StartedQpc;
        public Dictionary<ulong, (Vector3 Position, float Heading)> Replies = new();
        public List<PartyChoice> Roster = new();
    }
    private sealed class StageRecallSession
    {
        public string Nonce = "";
        public string RosterTag = "";
        public uint Territory;
        public StageFormationPreset Preset = new();
        public List<PartyChoice> Roster = new();
        public Queue<(ulong Member, int Slot, Vector3 Dest, float Heading)> Pending = new();
        public HashSet<ulong> Arrived = new();
        public Dictionary<ulong, string> Errors = new();
        public long StartedQpc;
        public long LastSendQpc;
        public bool LeaderMoveIssued;
    }

    private readonly StageFormationNavigator stageNavigator = new(Plugin.PluginInterface);
    private string stageName = "My Octet Stage";
    private string selectedStageId = "";
    private string stageStatus = "Save a formation after positioning your Bards on stage.";
    private StageCaptureSession? capturingStage;
    private StageRecallSession? recallingStage;
    private (string Nonce, uint Territory, long DueQpc)? stageCaptureReply;
    private (string Nonce, bool Arrived, string Reason, long DueQpc)? stageMovementReply;
    private string stageMovementNonce = "";
    private bool stageMoveReported;
    private bool stageDeletePending;
    private string lastRecallStageNonce = "";
    private string lastRejectedStageCaptureNonce = "";

    private static string StageNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    private static double StageElapsed(long qpc) => (Stopwatch.GetTimestamp() - qpc) / (double)Stopwatch.Frequency;
    private static long StageAfter(int milliseconds) => Stopwatch.GetTimestamp() +
        (long)(milliseconds / 1000.0 * Stopwatch.Frequency);
    private static string StageRosterTag(IReadOnlyList<PartyChoice> party) =>
        OneClickEnsembleCue.RosterTag(party.Select(x => x.ContentId).ToList());
    private List<PartyChoice> StageRoster() => PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
        .OrderBy(x => x.ContentId).ToList();
    private uint StageTerritory => Plugin.ClientState.TerritoryType;
    private bool StageSafe(out string error)
    {
        if (Plugin.Objects.LocalPlayer is null) { error = "Character is not loaded."; return false; }
        if (Plugin.Conditions[ConditionFlag.InCombat] || Plugin.Conditions[ConditionFlag.Performing] ||
            plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
            plugin.Instruments.InProgress)
        { error = "Stop the performance, stow the instrument and leave combat before moving Bards."; return false; }
        error = "";
        return true;
    }
    private bool StageLeader(out string reason)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor ||
            !cfg.GameEnsembleEnabled || !cfg.NativeLeaderSyncEnabled)
        { reason = "Select native Ensemble Conductor and enable party-leader synchronisation."; return false; }
        if (Plugin.Party.Length < 2 || Plugin.Party.PartyLeaderIndex >= (uint)Plugin.Party.Length ||
            Plugin.Party[(int)Plugin.Party.PartyLeaderIndex]?.ContentId !=
                PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party))
        { reason = "Only the actual FFXIV party leader can save and recall party formations."; return false; }
        reason = ""; return true;
    }
    // Territory alone is not a sufficient proximity check: housing wards and
    // instanced stages can share the same territory and world coordinates.
    // Require the actual in-world party leader actor to be visible nearby.
    private bool StageLeaderIsNearby()
    {
        var local = Plugin.Objects.LocalPlayer;
        var members = Plugin.Party;
        if (local is null || members.PartyLeaderIndex >= (uint)members.Length) return false;
        var leader = members[(int)members.PartyLeaderIndex];
        if (leader is null || leader.EntityId == 0) return false;
        var leaderObject = Plugin.Objects.FirstOrDefault(o => o != null && o.EntityId == leader.EntityId);
        return leaderObject is not null && Vector3.Distance(leaderObject.Position, local.Position) <= 45f;
    }

    private (Vector3 Position, float Heading) StageLocalPose()
    {
        var player = Plugin.Objects.LocalPlayer ?? throw new InvalidOperationException("No local player available.");
        return (player.Position, player.Rotation);
    }

    private void SaveStageFormation()
    {
        try
        {
            if (!StageLeader(out var why) || !StageSafe(out why)) throw new InvalidOperationException(why);
            if (capturingStage is not null || recallingStage is not null)
                throw new InvalidOperationException("A capture or recall is already active.");
            if (string.IsNullOrWhiteSpace(stageName) || stageName.Trim().Length > 64)
                throw new InvalidOperationException("Choose a formation name of 1 to 64 characters.");
            var roster = StageRoster();
            if (roster.Count is < 2 or > 15) throw new InvalidOperationException("A formation needs 2–15 party members.");
            var (pos, facing) = StageLocalPose();
            var localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
            var session = new StageCaptureSession {
                Nonce = StageNonce(), Name = stageName.Trim(), Roster = roster,
                Territory = StageTerritory, Anchor = pos, Heading = facing,
                StartedQpc = Stopwatch.GetTimestamp()
            };
            session.Replies.Add(localId, (pos, facing));
            string cmd = GamePartyCueProtocol.FormationCapture(session.Nonce, StageRosterTag(roster), session.Territory);
            if (!plugin.PartyChat.TryBroadcast(cmd, out why)) throw new InvalidOperationException(why);
            capturingStage = session;
            stageStatus = $"Requesting stage positions from {roster.Count - 1} Bards. Each follower must allow stage movements.";
        }
        catch (Exception ex) { stageStatus = "Could not save stage formation: " + ex.Message; }
    }

    private void ReceiveStageCapture(GamePartyCue cue)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) return;
        if (!cfg.AllowLeaderStageMovement)
        {
            if (lastRejectedStageCaptureNonce != cue.Nonce)
            {
                lastRejectedStageCaptureNonce = cue.Nonce;
                plugin.PartyChat.TryReply(GamePartyCueProtocol.FormationFailed(cue.Nonce, "NOOPT"), out _);
            }
            stageStatus = "The conductor requested a formation capture, but remote positioning is disabled.";
            return;
        }
        var roster = StageRoster();
        if (roster.Count < 2 || !string.Equals(cue.RosterTag, StageRosterTag(roster), StringComparison.OrdinalIgnoreCase) ||
            cue.FormationTerritory != StageTerritory) return;
        if (!StageLeaderIsNearby())
        {
            plugin.PartyChat.TryReply(GamePartyCueProtocol.FormationFailed(cue.Nonce, "DISTANT"), out _);
            stageStatus = "Cannot capture a stage in another housing ward or instance."; return;
        }
        if (!StageSafe(out var why))
        {
            plugin.PartyChat.TryReply(GamePartyCueProtocol.FormationFailed(cue.Nonce, "BUSY"), out _);
            stageStatus = why; return;
        }
        int slot = roster.FindIndex(p => p.IsLocal);
        if (slot < 0) return;
        // Stagger 7 follower replies so octet parties don't hit chat flood limits.
        stageCaptureReply = (cue.Nonce, cue.FormationTerritory, StageAfter(400 + slot * 450));
        stageStatus = "Conductor requested our current stage position; replying automatically.";
    }

    private void ReceiveStagePosition(GamePartyCue cue, ulong sender)
    {
        if (capturingStage is not { } session || !string.Equals(cue.Nonce, session.Nonce, StringComparison.Ordinal) ||
            !session.Roster.Any(x => x.ContentId == sender) || cue.FormationTerritory != session.Territory ||
            session.Replies.ContainsKey(sender)) return;
        if (Vector3.Distance(session.Anchor, cue.FormationPosition) > StageFormationMath.MaxCaptureRadius)
        { stageStatus = "Bard outside 32m stage capture radius; move everybody closer and save again."; return; }
        session.Replies[sender] = (cue.FormationPosition, cue.FormationHeading);
        stageStatus = $"Saving stage: {session.Replies.Count}/{session.Roster.Count} performers reported their positions.";
        if (session.Replies.Count == session.Roster.Count) FinishStageCapture(session);
    }

    private void FinishStageCapture(StageCaptureSession session)
    {
        try
        {
            var preset = new StageFormationPreset {
                Name = session.Name, TerritoryId = session.Territory,
                Anchor = session.Anchor, AnchorHeading = session.Heading,
                Members = session.Roster.Select(m => {
                    var (pos, heading) = session.Replies[m.ContentId];
                    return StageFormationMath.Capture(m.ContentId, m.Name, pos, heading, session.Anchor, session.Heading);
                }).ToList()
            };
            StageFormationMath.Validate(preset);
            cfg.StageFormations ??= new();
            if (cfg.StageFormations.Count >= 32) throw new InvalidOperationException("Stage library is full (32 formations).");
            cfg.StageFormations.Add(preset);
            cfg.SelectedStageFormationId = preset.Id;
            selectedStageId = preset.Id;
            cfg.Save();
            stageStatus = $"Saved '{preset.Name}' with {preset.Members.Count} Bard positions. Recalling never auto-starts music.";
        }
        catch (Exception ex) { stageStatus = "Could not save formation: " + ex.Message; }
        finally { capturingStage = null; }
    }

    private StageFormationPreset? SelectedStage() => cfg.StageFormations?.FirstOrDefault(x => x.Id == selectedStageId);

    private void RecallStageFormation()
    {
        try
        {
            if (!StageLeader(out var why) || !StageSafe(out why)) throw new InvalidOperationException(why);
            if (capturingStage is not null || recallingStage is not null)
                throw new InvalidOperationException("Another formation request is already active. Stop it first.");
            var preset = SelectedStage() ?? throw new InvalidOperationException("Select a saved stage formation first.");
            StageFormationMath.Validate(preset);
            if (preset.TerritoryId != StageTerritory) throw new InvalidOperationException("Your character is not in the saved stage's territory.");
            var roster = StageRoster();
            if (!roster.Select(x => x.ContentId).OrderBy(x => x)
                .SequenceEqual(preset.Members.Select(x => x.ContentId).OrderBy(x => x)))
                throw new InvalidOperationException("The current party differs from this saved formation. Save a new formation for this lineup.");
            var (here, _) = StageLocalPose();
            if (Vector3.Distance(here, preset.Anchor) > 20f)
                throw new InvalidOperationException("Approach within 20m of the saved stage anchor before recalling.");
            var session = new StageRecallSession {
                Nonce = StageNonce(), Preset = preset, Territory = StageTerritory, Roster = roster,
                RosterTag = StageRosterTag(roster), StartedQpc = Stopwatch.GetTimestamp()
            };
            foreach (var p in roster.Where(x => !x.IsLocal))
            {
                int slot = roster.FindIndex(x => x.ContentId == p.ContentId) + 1;
                var member = preset.Members.First(m => m.ContentId == p.ContentId);
                Vector3 target = StageFormationMath.Destination(member, preset.Anchor, preset.AnchorHeading);
                session.Pending.Enqueue((p.ContentId, slot, target, StageFormationMath.DesiredHeading(member, preset.AnchorHeading)));
            }
            recallingStage = session;
            lastRecallStageNonce = session.Nonce;
            stageMoveReported = false;
            stageStatus = $"Recalling '{preset.Name}' for {roster.Count} players. Waiting for vnavmesh on each client.";
        }
        catch (Exception ex) { stageStatus = "Could not recall formation: " + ex.Message; }
    }

    private void ReceiveStageMove(GamePartyCue cue)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) return;
        var roster = StageRoster();
        if (cue.TargetSlot != roster.FindIndex(x => x.IsLocal) + 1) return;
        if (stageMovementNonce == cue.Nonce && (stageNavigator.Active || stageMoveReported)) return;
        stageMovementNonce = cue.Nonce;
        stageMoveReported = false;
        if (!cfg.AllowLeaderStageMovement)
        { QueueStageReply(cue.Nonce, false, "NOOPT"); return; }
        if (!string.Equals(cue.RosterTag, StageRosterTag(roster), StringComparison.OrdinalIgnoreCase) ||
            StageTerritory != cue.FormationTerritory)
        { QueueStageReply(cue.Nonce, false, "ZONE"); return; }
        if (!StageLeaderIsNearby()) { QueueStageReply(cue.Nonce, false, "DISTANT"); return; }
        if (!StageSafe(out _)) { QueueStageReply(cue.Nonce, false, "BUSY"); return; }
        if (stageNavigator.Active) { QueueStageReply(cue.Nonce, false, "MOVING"); return; }
        stageMoveReported = false;
        var (here, _) = StageLocalPose();
        if (!stageNavigator.TryStart(here, cue.FormationPosition, out string reason))
        { stageStatus = reason; QueueStageReply(cue.Nonce, false, "NAVFAIL"); return; }
        stageStatus = reason + " Desired facing: " + (cue.FormationHeading * 180f / MathF.PI).ToString("0") + "°.";
        if (stageNavigator.Arrived) QueueStageReply(cue.Nonce, true, "");
    }

    private void QueueStageReply(string nonce, bool arrived, string reason)
    {
        int slot = StageRoster().FindIndex(x => x.IsLocal);
        stageMovementReply = (nonce, arrived, reason, StageAfter(280 + Math.Max(0, slot) * 320));
        stageMoveReported = true;
    }

    private void HandleStageReply(GamePartyCue cue, ulong sender)
    {
        if (cue.Kind == GamePartyCueKind.FormationPosition) { ReceiveStagePosition(cue, sender); return; }
        if (capturingStage is { } capture && cue.Kind == GamePartyCueKind.FormationFailed && cue.Nonce == capture.Nonce)
        { stageStatus = "Capture aborted: a party member was unable to report their position."; capturingStage = null; return; }
        if (recallingStage is not { } recall || recall.Nonce != cue.Nonce ||
            !recall.Roster.Any(x => x.ContentId == sender)) return;
        if (cue.Kind == GamePartyCueKind.FormationArrived)
        { recall.Arrived.Add(sender); recall.Errors.Remove(sender); }
        else if (cue.Kind == GamePartyCueKind.FormationFailed)
            recall.Errors[sender] = cue.PackedInstruments;
    }

    private void StopStageFormation()
    {
        string nonce = recallingStage?.Nonce ?? (string.IsNullOrEmpty(stageMovementNonce) ? lastRecallStageNonce : stageMovementNonce);
        if (!string.IsNullOrEmpty(nonce) && StageLeader(out _))
            plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.FormationStop(nonce), out _);
        stageNavigator.Stop();
        capturingStage = null;
        recallingStage = null;
        stageMovementReply = null;
        stageCaptureReply = null;
        stageMovementNonce = "";
        lastRecallStageNonce = "";
        stageStatus = "Stage positioning stopped. MIDI playback and playlist are unchanged.";
    }

    private void ReceiveStageStop(GamePartyCue cue)
    {
        if (!string.Equals(stageMovementNonce, cue.Nonce, StringComparison.OrdinalIgnoreCase)) return;
        stageNavigator.Stop("Conductor cancelled stage movement.");
        stageMovementReply = null;
        stageMovementNonce = "";
        stageMoveReported = false;
        stageStatus = "Conductor stopped stage positioning.";
    }

    public void PollStageFormations(string why1)
    {
        bool safe = StageSafe(out _);
        if (Plugin.Objects.LocalPlayer is { } local)
            stageNavigator.Poll(local.Position, safe);
        if (stageCaptureReply is { } reply && Stopwatch.GetTimestamp() >= reply.DueQpc)
        {
            stageCaptureReply = null;
            if (StageTerritory == reply.Territory && StageSafe(out _))
            {
                var (pos, facing) = StageLocalPose();
                if (!plugin.PartyChat.TryReply(GamePartyCueProtocol.FormationPosition(reply.Nonce,
                    reply.Territory, pos, facing), out var why)) stageStatus = "Cannot report position: " + why;
            }
        }
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
            !stageMoveReported && !string.IsNullOrEmpty(stageMovementNonce) && !stageNavigator.Active)
        {
            QueueStageReply(stageMovementNonce, stageNavigator.Arrived, stageNavigator.Arrived ? "" : "NAVFAIL");
            stageStatus = stageNavigator.Status;
        }
        if (stageMovementReply is { } ack && Stopwatch.GetTimestamp() >= ack.DueQpc)
        {
            stageMovementReply = null;
            string text = ack.Arrived ? GamePartyCueProtocol.FormationArrived(ack.Nonce) :
                GamePartyCueProtocol.FormationFailed(ack.Nonce, ack.Reason);
            if (!plugin.PartyChat.TryReply(text, out var why))
            { stageStatus = "Could not acknowledge stage positioning: " + why; }
        }
        if (capturingStage is { } capture && StageElapsed(capture.StartedQpc) > 22)
        {
            string missing = string.Join(", ", capture.Roster.Where(p => !capture.Replies.ContainsKey(p.ContentId)).Select(p => p.Name));
            stageStatus = "Stage capture timed out. Missing: " + missing;
            capturingStage = null;
        }
        if (recallingStage is not { } session) return;
        if (!session.LeaderMoveIssued)
        {
            session.LeaderMoveIssued = true;
            var member = session.Preset.Members.First(m => m.ContentId == PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party));
            var (here, _) = StageLocalPose();
            Vector3 target = StageFormationMath.Destination(member, session.Preset.Anchor, session.Preset.AnchorHeading);
            if (!StageSafe(out _) || !stageNavigator.TryStart(here, target, out string why))
            {
                stageStatus = $"Conductor cannot move: {why1}";
                recallingStage = null;
                return;
            }
            if (stageNavigator.Arrived) session.Arrived.Add(member.ContentId);
        }
        if (session.Pending.Count > 0 &&
            (session.LastSendQpc == 0 || StageElapsed(session.LastSendQpc) > .9))
        {
            var item = session.Pending.Peek();
            string cmd = GamePartyCueProtocol.FormationMove(session.Nonce,session.RosterTag,item.Slot,
                session.Territory,item.Dest,item.Heading);
            if (plugin.PartyChat.TryBroadcast(cmd,out var why))
            {
                session.Pending.Dequeue();
                session.LastSendQpc = Stopwatch.GetTimestamp();
            }
            else
            {
                stageStatus = "Stage command could not be sent: " + why;
                recallingStage = null; stageNavigator.Stop(); return;
            }
        }
        ulong leaderId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (!stageNavigator.Active && stageNavigator.Arrived) session.Arrived.Add(leaderId);
        if (session.Errors.Count > 0)
        {
            stageStatus = "Stage movement problems: " + string.Join(", ", session.Errors.Select(x =>
                session.Roster.FirstOrDefault(p => p.ContentId == x.Key)?.Name + " (" + x.Value + ")"));
        }
        else stageStatus = $"Moving orchestra: {session.Arrived.Count}/{session.Roster.Count} Bards at their stage spots.";
        if (session.Pending.Count == 0 && session.Arrived.Count == session.Roster.Count)
        {
            recallingStage = null;
            stageStatus = $"Formation '{session.Preset.Name}' reached by {session.Roster.Count} Bards! " +
                "Facing is shown as a guide and is not automatically forced.";
        }
        else if (StageElapsed(session.StartedQpc) > 80)
        {
            stageStatus = "Stage positioning timed out. " + stageStatus + " Press Stop Movement to cancel any remaining walking.";
            recallingStage = null; stageNavigator.Stop();
        }
    }

    private void DrawStageFormations()
    {
        if (string.IsNullOrEmpty(selectedStageId)) selectedStageId = cfg.SelectedStageFormationId ?? "";
        ImGui.TextColored(new Vector4(.96f,.59f,.83f,1f),Loc.T("STAGE FORMATIONS — SAVE & RECALL"));
        ImGui.TextWrapped(Loc.T("Save each Bard's position, then walk the party back to those spots on stage. " +
            "Requires vnavmesh on every client. Only your party leader can send stage commands."));
        ImGui.TextDisabled(Loc.T("Movement is walking only, limited to nearby spots; doesn't teleport, play music or force facing."));
        ImGui.TextColored(stageNavigator.IsAvailable ? new Vector4(.45f,.93f,.65f,1f) : new Vector4(1f,.68f,.39f,1f),
            Loc.T(stageNavigator.IsAvailable ? "vnavmesh installed (local client)" : "vnavmesh not installed on this client"));
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
        {
            bool permit = cfg.AllowLeaderStageMovement;
            if (ImGui.Checkbox(Loc.T("Allow my party leader to save my position and move me on stage##stageConsent"), ref permit))
            { cfg.AllowLeaderStageMovement = permit; cfg.Save(); }
            ImGui.TextDisabled(Loc.T("One-time opt-in. Disable it any time; no movement occurs on login or song load."));
            if (ImGui.Button(Loc.T("STOP MY STAGE MOVEMENT")))
            {
                if (!string.IsNullOrEmpty(stageMovementNonce))
                    QueueStageReply(stageMovementNonce, false, "STOPPED");
                stageNavigator.Stop();
                stageMovementNonce = "";
            }
        }
        else if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
        {
            ImGui.SetNextItemWidth(270);
            ImGui.InputText(Loc.T("Formation name##stageName"), ref stageName, 64);
            if (ImGui.Button(Loc.T("SAVE CURRENT PARTY POSITIONS"))) SaveStageFormation();
            ImGui.TextDisabled(Loc.T("Position everyone manually once, then Save. All party members must opt in."));
            if (ImGui.BeginCombo(Loc.T("Saved formations##stageLibrary"),
                SelectedStage()?.Name ?? Loc.T("Choose a saved formation")))
            {
                foreach (var preset in cfg.StageFormations ?? new())
                    if (ImGui.Selectable(preset.Name + "##stage" + preset.Id, selectedStageId == preset.Id))
                    {
                        selectedStageId = preset.Id;
                        cfg.SelectedStageFormationId = preset.Id;
                        cfg.Save();
                        // Selecting a preset in the old dropdown also refreshes
                        // the new canvas, preventing accidental edits to an old draft.
                        LoadVisualStage();
                    }
                ImGui.EndCombo();
            }
            // Visual editor can create/modify party positions without first positioning
            // every character manually. Save Current Positions above remains available.
            DrawVisualStageDesigner();
            if (ImGui.Button(Loc.T("MOVE PARTY INTO SAVED FORMATION"))) RecallStageFormation();
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("STOP STAGE MOVEMENT"))) StopStageFormation();
            var selected = SelectedStage();
            if (selected is not null)
            {
                ImGui.TextDisabled(Loc.T($"Saved for {selected.Members.Count} Bards / territory {selected.TerritoryId}."));
                if (ImGui.TreeNode(Loc.T("Preview saved stage positions")))
                {
                    foreach (var member in selected.Members)
                        ImGui.TextUnformatted($"{member.Name}  X:{member.Offset.X:+0.0;-0.0;0} " +
                            $"Z:{member.Offset.Z:+0.0;-0.0;0}  Facing:{member.HeadingOffset*180f/MathF.PI:+0;-0;0}°");
                    ImGui.TreePop();
                }
                if (ImGui.SmallButton(Loc.T("Delete selected formation..."))) stageDeletePending = true;
                if (stageDeletePending)
                {
                    ImGui.TextWrapped(Loc.T("Delete this saved formation? MIDI files and concert presets are not affected."));
                    if (ImGui.Button(Loc.T("CONFIRM DELETE FORMATION")))
                    {
                        cfg.StageFormations.RemoveAll(x => x.Id == selected.Id);
                        selectedStageId = cfg.StageFormations.FirstOrDefault()?.Id ?? "";
                        cfg.SelectedStageFormationId = selectedStageId;
                        cfg.Save(); stageDeletePending = false;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Loc.T("Cancel##stageDelete"))) stageDeletePending = false;
                }
            }
        }
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T(stageStatus));
        ImGui.TextDisabled(Loc.T("This saves relative positions and a facing guide. Automatic facing is not yet implemented."));
    }

    internal void StopStageMovementOnUnload() => stageNavigator.Stop("Plugin unloaded; stage movement stopped.");
}
