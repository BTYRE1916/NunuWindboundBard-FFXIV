using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>Between-song, explicit and interruptible local/group stage emotes.</summary>
public sealed partial class MainWindow
{
    private readonly ShowmasterPlayer showmaster = new();
    private int showSelectedId = 1;
    private int showNewStepIndex;
    private string showStatus = "Stow instruments, then choose an intermission routine.";
    private string showGroupNonce = "";
    private string showLastReceivedNonce = "";
    private bool ShowmasterSafe(out string why)
    {
        if (Plugin.Objects.LocalPlayer is null) { why = "Log into the game first."; return false; }
        if (Plugin.Conditions[ConditionFlag.InCombat] || Plugin.Conditions[ConditionFlag.Performing] ||
            plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
            plugin.Instruments.InProgress || plugin.Instruments.CurrentId != 0)
        { why = "Finish the song and fully stow the instrument before starting an emote routine."; return false; }
        why = "";
        return true;
    }
    private List<ShowmasterRoutine> ShowRoutines()
    {
        cfg.ShowmasterRoutines ??= new();
        if (cfg.ShowmasterRoutines.Count == 0)
        { cfg.ShowmasterRoutines = ShowmasterCodec.Defaults(); cfg.Save(); }
        return cfg.ShowmasterRoutines;
    }
    public void PollShowmaster()
    {
        if (!showmaster.Active) return;
        bool safe = ShowmasterSafe(out _);
        showmaster.Tick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), safe,
            emote => ShowmasterEmoteSender.TrySend(emote, out showStatus));
        if (!showmaster.Active) showGroupNonce = "";
    }
    private void StartLocalShowmaster(ShowmasterRoutine routine)
    {
        try
        {
            if (!ShowmasterSafe(out string why)) throw new InvalidOperationException(why);
            ShowmasterCodec.Validate(routine.Steps);
            showGroupNonce = "";
            showmaster.Start(routine.Steps, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 180);
            showStatus = $"Local routine '{routine.Name}' queued.";
        }
        catch (Exception ex) { showStatus = "Cannot start routine: " + ex.Message; }
    }

    private void StartGroupShowmaster(ShowmasterRoutine routine)
    {
        try
        {
            if (!ShowmasterSafe(out string why)) throw new InvalidOperationException(why);
            if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor || !cfg.GameEnsembleEnabled)
                throw new InvalidOperationException("Choose Native Ensemble Conductor and enable Game Ensemble.");
            var roster = StageRoster();
            if (roster.Count < 2) throw new InvalidOperationException("Form a party first.");
            string nonce = StageNonce();
            long startAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2200;
            string command = GamePartyCueProtocol.ShowmasterStart(nonce, StageRosterTag(roster),
                startAt, ShowmasterCodec.Encode(routine.Steps));
            if (!plugin.PartyChat.TryBroadcast(command, out why))
                throw new InvalidOperationException(why);
            showmaster.Start(routine.Steps, startAt);
            showGroupNonce = nonce;
            showStatus = "Sent synchronized stage routine. Followers must allow leader Showmaster cues.";
        }
        catch (Exception ex) { showStatus = "Could not start group routine: " + ex.Message; }
    }

    private void ReceiveShowmasterStart(GamePartyCue cue)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !cfg.AllowLeaderShowmaster) return;
        if (cue.Nonce == showLastReceivedNonce) return;
        if (!string.Equals(cue.RosterTag, StageRosterTag(StageRoster()), StringComparison.OrdinalIgnoreCase))
        { showStatus = "Ignored choreography for a different party roster."; return; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (cue.StartUnixMs < now - 1500 || cue.StartUnixMs > now + 9000)
        { showStatus = "Ignored expired or distant show cue."; return; }
        if (!ShowmasterSafe(out string why)) { showStatus = "Ignored show cue: " + why; return; }
        if (!ShowmasterCodec.TryDecode(cue.ShowmasterSteps, out var steps))
        { showStatus = "Ignored invalid show routine."; return; }
        showLastReceivedNonce = cue.Nonce;
        showmaster.Start(steps, cue.StartUnixMs);
        showGroupNonce = cue.Nonce;
        showStatus = "Conductor's intermission choreography queued.";
    }
    private void ReceiveShowmasterStop(GamePartyCue cue)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !cfg.AllowLeaderShowmaster) return;
        if (showGroupNonce != cue.Nonce) return;
        showmaster.Stop("Conductor stopped the show routine.");
        showGroupNonce = "";
    }
    private void StopShowmaster()
    {
        if (!string.IsNullOrEmpty(showGroupNonce) &&
            (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
            plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.ShowmasterStop(showGroupNonce), out _);
        showmaster.Stop();
        showGroupNonce = "";
        showStatus = "Showmaster stopped. MIDI and playlists are unchanged.";
    }

    private void DrawShowmaster()
    {
        ImGui.TextColored(new System.Numerics.Vector4(.97f,.55f,.77f,1f), "WINDBOUND SHOWMASTER — INTERMISSION CHOREOGRAPHY");
        ImGui.TextWrapped("Use one-click emotes or timed emote combinations BETWEEN songs. " +
            "For group choreography, the conductor sends a small, validated routine to opted-in followers in party chat.");
        ImGui.TextDisabled("Never runs during performance; all instruments must be stowed. Commands use /emote motion (no chat spam).");
        if (ImGui.Button("STOW MY INSTRUMENT##showmaster"))
        {
            try
            {
                if (plugin.Player.IsPlaying) throw new InvalidOperationException("Stop the MIDI first.");
                plugin.Instruments.RequestUnequip();
                showStatus = "Stowing instrument via native game action...";
            }
            catch (Exception ex) { showStatus = ex.Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button("STOP SHOW ROUTINE##showmaster")) StopShowmaster();
        bool canRun = ShowmasterSafe(out var blocked);
        ImGui.TextColored(canRun ? new System.Numerics.Vector4(.5f,.95f,.65f,1f) :
            new System.Numerics.Vector4(1f,.72f,.4f,1f), canRun ? "Intermission: READY" : blocked);

        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
        {
            bool allow = cfg.AllowLeaderShowmaster;
            if (ImGui.Checkbox("Allow verified party leader's group emotes##showmaster", ref allow))
            { cfg.AllowLeaderShowmaster = allow; cfg.Save(); }
            ImGui.TextDisabled("Off by default. Disable any time; it never starts on login or MIDI load.");
        }
        ImGui.Separator();
        var routines = ShowRoutines();
        foreach (var routine in routines.ToArray())
        {
            if (routine.Steps is null || routine.Steps.Count == 0) continue;
            ImGui.PushID($"show{routine.Id}");
            if (ImGui.RadioButton($"{routine.Name}##pick", routine.Id == showSelectedId)) showSelectedId = routine.Id;
            ImGui.SameLine();
            if (ImGui.SmallButton("Run local")) StartLocalShowmaster(routine);
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("GROUP SHOW")) StartGroupShowmaster(routine);
            }
            ImGui.PopID();
        }
        if (ImGui.Button("Create custom routine##showmaster"))
        {
            int id = Enumerable.Range(1, 12).FirstOrDefault(i => routines.All(r => r.Id != i));
            if (id != 0)
            {
                routines.Add(new ShowmasterRoutine { Id = id, Name = "My Intermission", Steps = new() {
                    new() { Emote = "bow", DelayAfterMs = 1000 } } });
                showSelectedId = id; cfg.Save();
            }
            else showStatus = "Showmaster's library is full (12 routines).";
        }
        var selected = routines.FirstOrDefault(r => r.Id == showSelectedId);
        if (selected is not null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"EDIT ROUTINE #{selected.Id}");
            string title = selected.Name;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText("Routine name##showmaster", ref title, 48))
            { selected.Name = title; cfg.Save(); }
            if (selected.Steps is null) selected.Steps = new();
            for (int i = 0; i < selected.Steps.Count; i++)
            {
                var step = selected.Steps[i];
                ImGui.PushID($"editshow{i}");
                ImGui.TextUnformatted($"{i+1:00}:"); ImGui.SameLine();
                ImGui.SetNextItemWidth(130);
                if (ImGui.BeginCombo("##emote", "/" + step.Emote))
                {
                    foreach (string emote in ShowmasterCodec.AllowedEmotes)
                        if (ImGui.Selectable("/" + emote + "##" + emote, step.Emote == emote))
                        { step.Emote = emote; cfg.Save(); }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                int delay = step.DelayAfterMs;
                ImGui.SetNextItemWidth(125);
                if (ImGui.SliderInt("Wait ms", ref delay, 0, 5000))
                { step.DelayAfterMs = delay; cfg.Save(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove step"))
                { selected.Steps.RemoveAt(i); cfg.Save(); ImGui.PopID(); break; }
                ImGui.PopID();
            }
            ImGui.SetNextItemWidth(145);
            var actions = ShowmasterCodec.AllowedEmotes;
            if (ImGui.BeginCombo("Add emote##showmaster", "/" + actions[showNewStepIndex]))
            {
                for (int i = 0; i < actions.Count; i++)
                    if (ImGui.Selectable("/" + actions[i] + "##shownew" + i, i == showNewStepIndex)) showNewStepIndex = i;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("ADD STEP##showmaster"))
            {
                if (selected.Steps.Count < 12)
                { selected.Steps.Add(new ShowmasterStep { Emote = actions[showNewStepIndex], DelayAfterMs = 1000 }); cfg.Save(); }
                else showStatus = "Maximum 12 steps per routine.";
            }
            if (ImGui.SmallButton("Delete selected routine##showmaster"))
            { routines.Remove(selected); showSelectedId = routines.FirstOrDefault()?.Id ?? 0; cfg.Save(); }
        }
        ImGui.Separator();
        if (showmaster.Active)
            ImGui.ProgressBar(showmaster.TotalSteps == 0 ? 0 : (float)showmaster.CompletedSteps / showmaster.TotalSteps,
                new System.Numerics.Vector2(-1,0), $"{showmaster.CompletedSteps}/{showmaster.TotalSteps}");
        ImGui.TextWrapped(showStatus);
        ImGui.TextDisabled(showmaster.Status);
        ImGui.TextDisabled("Group emotes share a compact routine over existing FFXIV party chat. All clients need the same Windbound version; no images, files or arbitrary scripts are sent.");
    }
}
