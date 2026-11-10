using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Between-song Showmaster. The conductor selects from their own unlocked FFXIV
/// emotes. Followers independently enforce their *own* unlocks. Group messages
/// contain only emote IDs and timing and are authorized by party leader checks.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class ShowEmoteCheckSession
    {
        public string Nonce = "";
        public string RosterTag = "";
        public int StepCount;
        public string RoutineName = "";
        public List<PartyChoice> Roster = new();
        public Dictionary<ulong, int> Masks = new();
        public long StartedQpc;
    }

    private readonly ShowmasterPlayer showmaster = new();
    private readonly ShowmasterEmoteCatalog showCatalog = new();
    private int showSelectedId = 1;
    private uint showNewStepId;
    private string showEmoteSearch = "";
    private string showStatus = "Stow instruments, then choose an intermission routine.";
    private string showGroupNonce = "";
    private string showLastReceivedNonce = "";
    private string showLastCheckNonce = "";
    private ShowEmoteCheckSession? showCheckSession;
    private (string Nonce, int Mask, long DueQpc)? showCheckReply;

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

    private bool EnsureShowCatalog(bool refresh = false)
    {
        ulong id = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (refresh || !showCatalog.Loaded || showCatalog.ContentId != id)
            showCatalog.Refresh(id);
        return showCatalog.Loaded;
    }

    private List<ShowmasterRoutine> ShowRoutines()
    {
        cfg.ShowmasterRoutines ??= new();
        if (cfg.ShowmasterRoutines.Count == 0)
        { cfg.ShowmasterRoutines = ShowmasterCodec.Defaults(); cfg.Save(); }
        return cfg.ShowmasterRoutines;
    }

    private bool TryResolveShowSteps(ShowmasterRoutine routine, out List<ShowmasterStep> resolved, out string problem)
    {
        resolved = new();
        problem = "";
        if (!EnsureShowCatalog(refresh: true)) { problem = showCatalog.Status; return false; }
        try { ShowmasterCodec.Validate(routine.Steps); }
        catch (Exception error) { problem = error.Message; return false; }
        foreach (var step in routine.Steps)
        {
            ShowmasterEmoteEntry? entry = step.EmoteId != 0 ? showCatalog.Find(step.EmoteId) :
                showCatalog.FindCommand(step.Emote);
            if (entry is null)
            { problem = $"The game does not recognise emote '{step.Emote}' (ID {step.EmoteId})."; return false; }
            if (!entry.Unlocked)
            { problem = $"'{entry.Name}' isn't unlocked on the conductor. Choose a different emote."; return false; }
            resolved.Add(new ShowmasterStep {
                EmoteId = entry.Id, Emote = entry.Command.TrimStart('/'), DelayAfterMs = step.DelayAfterMs
            });
        }
        return true;
    }

    public void PollShowmaster()
    {
        if (showCheckReply is { } reply && System.Diagnostics.Stopwatch.GetTimestamp() >= reply.DueQpc)
        {
            showCheckReply = null;
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower && cfg.AllowLeaderShowmaster)
            {
                if (!plugin.PartyChat.TryReply(GamePartyCueProtocol.ShowmasterAvailability(reply.Nonce, reply.Mask),
                        out string error)) showStatus = "Could not reply to band check: " + error;
            }
        }
        if (!showmaster.Active) return;
        bool safe = ShowmasterSafe(out _);
        showmaster.TickWithSteps(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), safe, ExecuteShowStep);
        if (!showmaster.Active) showGroupNonce = "";
    }

    private ShowmasterExecutionResult ExecuteShowStep(ShowmasterStep step)
    {
        if (!EnsureShowCatalog())
        { showStatus = showCatalog.Status; return ShowmasterExecutionResult.Failed; }
        ShowmasterEmoteEntry? entry = step.EmoteId != 0 ? showCatalog.Find(step.EmoteId) :
            showCatalog.FindCommand(step.Emote);
        if (entry is { Unlocked: true })
        {
            if (!ShowmasterEmoteSender.TrySend(entry, out string detail))
            { showStatus = detail; return ShowmasterExecutionResult.Failed; }
            showStatus = detail;
            return ShowmasterExecutionResult.Performed;
        }
        // Another member's unlocks are never changed or bypassed. Fallback is
        // evaluated against *this* character's actual game unlock state.
        if (cfg.ShowmasterUseBowFallback)
        {
            var bow = showCatalog.FindCommand("bow");
            if (bow is { Unlocked: true } && ShowmasterEmoteSender.TrySend(bow, out _))
            {
                showStatus = $"'{entry?.Name ?? "Requested emote"}' unavailable here; bowed instead.";
                return ShowmasterExecutionResult.SkippedUnavailable;
            }
        }
        showStatus = $"Skipped unavailable emote ID {step.EmoteId} (not unlocked on this character).";
        return ShowmasterExecutionResult.SkippedUnavailable;
    }

    private void StartLocalShowmaster(ShowmasterRoutine routine)
    {
        try
        {
            if (!ShowmasterSafe(out string why)) throw new InvalidOperationException(why);
            if (!TryResolveShowSteps(routine, out var steps, out why)) throw new InvalidOperationException(why);
            showGroupNonce = "";
            showmaster.Start(steps, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 180);
            showStatus = $"Local routine '{routine.Name}' queued.";
        }
        catch (Exception ex) { showStatus = "Cannot start routine: " + ex.Message; }
    }

    private void StartGroupShowmaster(ShowmasterRoutine routine)
    {
        try
        {
            if (!ShowmasterSafe(out string why)) throw new InvalidOperationException(why);
            if (!StageLeader(out why)) throw new InvalidOperationException(why);
            if (!TryResolveShowSteps(routine, out var steps, out why)) throw new InvalidOperationException(why);
            var roster = StageRoster();
            if (roster.Count < 2) throw new InvalidOperationException("Form a party first.");
            string nonce = StageNonce();
            long startAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2200;
            string command = GamePartyCueProtocol.ShowmasterStartV2(nonce, StageRosterTag(roster),
                startAt, ShowmasterCodec.EncodeV2(steps));
            if (!plugin.PartyChat.TryBroadcast(command, out why))
                throw new InvalidOperationException(why);
            showmaster.Start(steps, startAt);
            showGroupNonce = nonce;
            showStatus = "Sent synchronized emote IDs. Other Bards must opt in and own each emote (or use fallback).";
        }
        catch (Exception ex) { showStatus = "Could not start group routine: " + ex.Message; }
    }

    private void CheckGroupShowmaster(ShowmasterRoutine routine)
    {
        try
        {
            if (!StageLeader(out string why)) throw new InvalidOperationException(why);
            if (!TryResolveShowSteps(routine, out var steps, out why)) throw new InvalidOperationException(why);
            var roster = StageRoster();
            if (roster.Count < 2) throw new InvalidOperationException("Form a party first.");
            string nonce = StageNonce();
            string tag = StageRosterTag(roster);
            string command = GamePartyCueProtocol.ShowmasterCheck(nonce, tag,
                ShowmasterCodec.EncodeV2(steps));
            if (!plugin.PartyChat.TryBroadcast(command, out why)) throw new InvalidOperationException(why);
            showCheckSession = new ShowEmoteCheckSession {
                Nonce = nonce, RosterTag = tag, StepCount = steps.Count,
                Roster = roster, RoutineName = routine.Name, StartedQpc = System.Diagnostics.Stopwatch.GetTimestamp()
            };
            showCheckSession.Masks[PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party)] =
                showCatalog.AvailabilityMask(steps);
            showStatus = "Checking the selected routine against each opted-in member's emote unlocks...";
        }
        catch (Exception ex) { showStatus = "Could not check band emotes: " + ex.Message; }
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
        List<ShowmasterStep> stepsV2;
        bool decoded;
        if (cue.Kind == GamePartyCueKind.ShowmasterStartV2)
            decoded = ShowmasterCodec.TryDecodeV2(cue.ShowmasterSteps, out stepsV2);
        else
            decoded = ShowmasterCodec.TryDecode(cue.ShowmasterSteps, out stepsV2);
        if (!decoded) { showStatus = "Ignored invalid show routine."; return; }
        if (!EnsureShowCatalog(refresh: true)) { showStatus = showCatalog.Status; return; }
        showLastReceivedNonce = cue.Nonce;
        showmaster.Start(stepsV2, cue.StartUnixMs);
        showGroupNonce = cue.Nonce;
        int available = 0;
        foreach (var step in stepsV2)
        {
            var emote = step.EmoteId != 0 ? showCatalog.Find(step.EmoteId) : showCatalog.FindCommand(step.Emote);
            if (emote?.Unlocked == true) available++;
        }
        showStatus = $"Conductor's routine queued: {available}/{stepsV2.Count} emotes available locally; missing steps use a fallback or skip.";
    }

    private void ReceiveShowmasterCheck(GamePartyCue cue)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !cfg.AllowLeaderShowmaster) return;
        if (cue.Nonce == showLastCheckNonce ||
            !string.Equals(cue.RosterTag, StageRosterTag(StageRoster()), StringComparison.OrdinalIgnoreCase)) return;
        if (!ShowmasterCodec.TryDecodeV2(cue.ShowmasterSteps, out var steps)) return;
        if (!EnsureShowCatalog(refresh: true)) return;
        showLastCheckNonce = cue.Nonce;
        int slot = StageRoster().FindIndex(p => p.IsLocal);
        int mask = showCatalog.AvailabilityMask(steps);
        showCheckReply = (cue.Nonce, mask, StageAfter(350 + Math.Max(0, slot) * 400));
        showStatus = $"Band check: {System.Numerics.BitOperations.PopCount((uint)mask)}/{steps.Count} requested emotes unlocked here.";
    }

    private void ReceiveShowmasterAvailability(GamePartyCue cue, ulong sender)
    {
        var session = showCheckSession;
        if (session is null || !session.Nonce.Equals(cue.Nonce, StringComparison.OrdinalIgnoreCase) ||
            !session.Roster.Any(x => x.ContentId == sender)) return;
        session.Masks[sender] = cue.ShowmasterMask;
        showStatus = $"Showmaster compatibility: {session.Masks.Count}/{session.Roster.Count} members reported.";
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

    private static bool ShowmasterEmoteSelectable(ShowmasterEmoteEntry e, bool selected, string key)
    {
        Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? wrap = null;
        if (e.IconId != 0)
        {
            try
            {
                if (Plugin.Textures.TryGetFromGameIcon(new GameIconLookup { IconId = e.IconId },
                        out var texture) && texture is not null)
                    texture.TryGetWrap(out wrap, out _);
            }
            catch { wrap = null; }
        }
        bool icon = wrap is not null;
        bool clicked = ImGui.Selectable((icon ? "      " : "") + $"{e.Name} ({e.Command})##{key}",
            selected, ImGuiSelectableFlags.None, new Vector2(0f, 26f));
        if (icon && wrap is not null)
        {
            Vector2 point = ImGui.GetItemRectMin() + new Vector2(3f, 2f);
            ImGui.GetWindowDrawList().AddImage(wrap.Handle, point, point + new Vector2(22f, 22f));
        }
        return clicked;
    }

    private void DrawShowmaster()
    {
        if (cfg.DarkStageThemeEnabled)
            DrawStageWorkspaceTitle("SHOWMASTER  /  BACKSTAGE DESK",
                "Choreographed bows, audience interaction and band-wide intermissions");
        else
            ImGui.TextColored(new Vector4(.97f,.55f,.77f,1f), "WINDBOUND SHOWMASTER — FULL FFXIV EMOTE LIBRARY");
        ImGui.TextWrapped("Plan a stage routine using the current conductor's unlocked FFXIV emotes. " +
            "Party members perform only emotes unlocked on their own character; the conductor cannot transfer unlocks.");
        ImGui.TextDisabled("Between songs only. Instruments must be stowed. No emotes run during MIDI playback.");
        if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PushAction(WindboundStageStyle.Cyan);
        try
        {
            if (ImGui.Button("STOW MY INSTRUMENT##showmaster", new Vector2(184f, 35f)))
            {
                try
                {
                    if (plugin.Player.IsPlaying) throw new InvalidOperationException("Stop the MIDI first.");
                    plugin.Instruments.RequestUnequip();
                    showStatus = "Stowing instrument via native game action...";
                }
                catch (Exception ex) { showStatus = ex.Message; }
            }
        }
        finally { if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PopAction(); }
        ImGui.SameLine();
        if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PushAction(WindboundStageStyle.Danger);
        try { if (ImGui.Button("STOP SHOW ROUTINE##showmaster", new Vector2(181f, 35f))) StopShowmaster(); }
        finally { if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PopAction(); }
        bool canRun = ShowmasterSafe(out var blocked);
        if (cfg.DarkStageThemeEnabled)
            WindboundStageStyle.Chip(canRun ? "INTERMISSION READY" : "STAGE BUSY",
                canRun ? WindboundStageStyle.Good : WindboundStageStyle.Warning, canRun ? "Ready for a routine." : blocked);
        else
            ImGui.TextColored(canRun ? new Vector4(.5f,.95f,.65f,1f) :
                new Vector4(1f,.72f,.4f,1f), canRun ? "Intermission: READY" : blocked);

        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
        {
            bool allow = cfg.AllowLeaderShowmaster;
            if (ImGui.Checkbox("Allow verified party leader's group emotes##showmaster", ref allow))
            { cfg.AllowLeaderShowmaster = allow; cfg.Save(); }
            bool fallback = cfg.ShowmasterUseBowFallback;
            if (ImGui.Checkbox("Bow instead of a missing/locked emote##showmaster", ref fallback))
            { cfg.ShowmasterUseBowFallback = fallback; cfg.Save(); }
            ImGui.TextDisabled("Every follower's unlock list is checked locally. Both options can be disabled at any time.");
        }

        bool loaded = EnsureShowCatalog();
        ImGui.Separator();
        ImGui.TextUnformatted("CURRENT CHARACTER'S GAME EMOTES");
        ImGui.TextWrapped(showCatalog.Status);
        if (ImGui.SmallButton("Refresh unlocked emotes##showmaster")) loaded = EnsureShowCatalog(refresh: true);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Search emotes##showmaster", ref showEmoteSearch, 80);
        var available = loaded ? showCatalog.Unlocked.Where(e =>
            string.IsNullOrEmpty(showEmoteSearch) || e.Name.Contains(showEmoteSearch, StringComparison.OrdinalIgnoreCase) ||
            e.Command.Contains(showEmoteSearch, StringComparison.OrdinalIgnoreCase)).ToArray() : Array.Empty<ShowmasterEmoteEntry>();
        if (cfg.DarkStageThemeEnabled)
        {
            WindboundStageStyle.Chip($"{available.Length} UNLOCKED EMOTES", WindboundStageStyle.Cyan);
            ImGui.SameLine();
            WindboundStageStyle.Chip($"{ShowRoutines().Count} SAVED ROUTINES", WindboundStageStyle.Rose);
        }
        else ImGui.TextDisabled($"{available.Length} unlocked emotes matching this search.");

        ImGui.Separator();
        var routines = ShowRoutines();
        foreach (var routine in routines.ToArray())
        {
            if (routine.Steps is null || routine.Steps.Count == 0) continue;
            ImGui.PushID($"show{routine.Id}");
            if (ImGui.RadioButton($"{routine.Name}##pick", routine.Id == showSelectedId)) showSelectedId = routine.Id;
            ImGui.SameLine();
            if (ImGui.Button("Run local", new Vector2(98f, 29f))) StartLocalShowmaster(routine);
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
            {
                ImGui.SameLine();
                if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PushAction(WindboundStageStyle.Accent(cfg.StageUiAccent));
                try
                {
                    if (ImGui.Button("GROUP SHOW", new Vector2(125f, 29f))) StartGroupShowmaster(routine);
                }
                finally { if (cfg.DarkStageThemeEnabled) WindboundStageStyle.PopAction(); }
            }
            ImGui.PopID();
            if (cfg.DarkStageThemeEnabled) ImGui.Separator();
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
            selected.Steps ??= new();
            for (int i = 0; i < selected.Steps.Count; i++)
            {
                var step = selected.Steps[i];
                ImGui.PushID($"editshow{i}");
                ImGui.TextUnformatted($"{i + 1:00}:"); ImGui.SameLine();
                var current = step.EmoteId != 0 ? showCatalog.Find(step.EmoteId) : showCatalog.FindCommand(step.Emote);
                string label = current?.Name ?? (step.EmoteId != 0 ? $"Emote #{step.EmoteId} (not available here)" :
                    "/" + step.Emote + " (legacy)");
                ImGui.SetNextItemWidth(218);
                if (ImGui.BeginCombo("##emote", label))
                {
                    foreach (var entry in available)
                        if (ShowmasterEmoteSelectable(entry, step.EmoteId == entry.Id, "select" + entry.Id))
                        { step.EmoteId = entry.Id; step.Emote = entry.Command.TrimStart('/'); cfg.Save(); }
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
            var chosen = showCatalog.Find(showNewStepId);
            string preview = chosen?.Name ?? "Choose any unlocked emote...";
            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("Add unlocked emote##showmaster", preview))
            {
                foreach (var entry in available)
                    if (ShowmasterEmoteSelectable(entry, showNewStepId == entry.Id, "new" + entry.Id))
                        showNewStepId = entry.Id;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("ADD STEP##showmaster"))
            {
                var entry = showCatalog.Find(showNewStepId);
                if (entry is null || !entry.Unlocked) showStatus = "Choose an unlocked emote first.";
                else if (selected.Steps.Count >= 12) showStatus = "Maximum 12 steps per routine.";
                else
                {
                    selected.Steps.Add(new ShowmasterStep { EmoteId = entry.Id,
                        Emote = entry.Command.TrimStart('/'), DelayAfterMs = 1000 });
                    cfg.Save();
                }
            }
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("CHECK BAND EMOTES##showmaster")) CheckGroupShowmaster(selected);
            }
            if (ImGui.SmallButton("Delete selected routine##showmaster"))
            { routines.Remove(selected); showSelectedId = routines.FirstOrDefault()?.Id ?? 0; cfg.Save(); }
        }
        if (showCheckSession is { } check)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"BAND EMOTE CHECK — {check.RoutineName}");
            ImGui.TextDisabled("Shows readiness for the selected routine only. Missing emotes are NOT transferred.");
            int full = (1 << check.StepCount) - 1;
            foreach (var m in check.Roster)
            {
                if (check.Masks.TryGetValue(m.ContentId, out int mask))
                {
                    int count = System.Numerics.BitOperations.PopCount((uint)(mask & full));
                    ImGui.TextUnformatted($"{m.Name}: {count}/{check.StepCount} emotes unlocked" +
                        (count == check.StepCount ? "  READY" : "  FALLBACK / SKIP"));
                }
                else ImGui.TextDisabled($"{m.Name}: awaiting an opted-in client's reply...");
            }
        }
        ImGui.Separator();
        if (showmaster.Active)
            ImGui.ProgressBar(showmaster.TotalSteps == 0 ? 0 : (float)showmaster.CompletedSteps / showmaster.TotalSteps,
                new Vector2(-1,0), $"{showmaster.CompletedSteps}/{showmaster.TotalSteps}");
        if (cfg.DarkStageThemeEnabled && showmaster.Active)
            WindboundStageStyle.Chip($"SHOW RUNNING  {showmaster.CompletedSteps}/{showmaster.TotalSteps}", WindboundStageStyle.Rose);
        ImGui.TextWrapped(showStatus);
        ImGui.TextDisabled(showmaster.Status);
        ImGui.TextDisabled("Conductor's library defines routines; each follower uses their own unlocks. " +
            "Only emote IDs, timings and one routine compatibility mask travel over party chat.");
    }
}
