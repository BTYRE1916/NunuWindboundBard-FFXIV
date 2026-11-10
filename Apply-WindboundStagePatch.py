#!/usr/bin/env python3
"""Non-destructive source patch for NunuWindboundBard v0.34/master.

Checks all original anchor points before modifying anything; saves original files
in a timestamped backup. It deliberately refuses to touch a source tree that
has already been patched or differs from the inspected revision.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parent

class PatchError(RuntimeError):
    pass

changes: dict[str, str] = {}

def load(repo: Path, path: str) -> str:
    if path not in changes:
        f = repo / path
        if not f.is_file():
            raise PatchError(f"Missing source file: {f}")
        changes[path] = f.read_text(encoding='utf-8-sig')
    return changes[path]

def replace(repo: Path, path: str, old: str, new: str, label: str) -> None:
    source = load(repo, path)
    matches = source.count(old)
    if matches != 1:
        raise PatchError(f"{label}: expected exactly one source match, found {matches}; no files changed."
                         " Work with an unmodified checkout of the inspected v0.34 source.")
    changes[path] = source.replace(old, new, 1)

def r(repo, path, old, new, label):
    replace(repo, path, old.strip('\n'), new.strip('\n'), label)

def stage(repo: Path):
    cfg = 'src/NunuWindboundBard/Configuration.cs'
    main = 'src/NunuWindboundBard/MainWindow.StageFormations.cs'
    designer = 'src/NunuWindboundBard/MainWindow.StageDesigner.cs'
    plugin = 'src/NunuWindboundBard/Plugin.cs'
    project = 'src/NunuWindboundBard/NunuWindboundBard.csproj'

    r(repo,cfg,
      'public int Version { get; set; } = 33;',
      'public int Version { get; set; } = 34;', 'configuration version')
    r(repo,cfg,
      'public bool AllowLeaderStageMovement { get; set; } = false;',
      '''public bool AllowLeaderStageMovement { get; set; } = false;
    // Each client can opt out. This applies only after explicit stage recall.
    public bool AutoFaceAfterStageMovement { get; set; } = true;
    // A local solo layout has no conductor/party dependency.
    public SoloStageLayout? SoloStage { get; set; }''', 'stage settings')

    r(repo,plugin,
      '[PluginService] internal static ICondition Conditions { get; private set; } = null!;',
      '''[PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;''',
      'game configuration service')
    r(repo,plugin,
      '''        Config.StageFormations ??= new();
        Config.ConcertSnapshots ??= new();''',
      '''        if (Config.Version < 34)
        {
            // Enable automatic facing only for already-authorized stage movement.
            // No extra party permissions and no movement on login.
            Config.AutoFaceAfterStageMovement = true;
            Config.Version = 34;
            Config.Save();
        }
        Config.StageFormations ??= new();
        Config.ConcertSnapshots ??= new();''',
      'stage migration')
    r(repo,project,'<Version>0.34.0.0</Version>','<Version>0.35.0.0</Version>',
      'assembly version')

    r(repo,main,'public bool LeaderMoveIssued;',
      '''public bool LeaderMoveIssued;
        public bool LeaderFaceIssued;''', 'conductor facing session')
    r(repo,main,
      'private readonly StageFormationNavigator stageNavigator = new(Plugin.PluginInterface);',
      '''private readonly StageFormationNavigator stageNavigator = new(Plugin.PluginInterface);
    private readonly StageFacingController stageFacing = new();
    private float? stageFollowerTargetHeading;
    private bool stageFollowerFaceIssued;''', 'facing controller')
    r(repo,main,
      '''            if (capturingStage is not null || recallingStage is not null)
                throw new InvalidOperationException("Another formation request is already active. Stop it first.");''',
      '''            if (capturingStage is not null || recallingStage is not null || soloStageWalking ||
                soloStageFacingPending || stageFacing.Active || stageNavigator.Active)
                throw new InvalidOperationException("Another stage action is already active. Stop it first.");''',
      'prevent simultaneous ensemble and solo moves')
    r(repo,main,
      '''        stageMovementNonce = cue.Nonce;
        stageMoveReported = false;
        if (!cfg.AllowLeaderStageMovement)''',
      '''        stageMovementNonce = cue.Nonce;
        stageMoveReported = false;
        stageFollowerTargetHeading = null;
        stageFollowerFaceIssued = false;
        if (!cfg.AllowLeaderStageMovement)''',
      'reset follower facing on new movement')
    r(repo,main,
      '''        if (stageNavigator.Active) { QueueStageReply(cue.Nonce, false, "MOVING"); return; }
        stageMoveReported = false;''',
      '''        if (stageNavigator.Active || stageFacing.Active || soloStageWalking || soloStageFacingPending)
        { QueueStageReply(cue.Nonce, false, "MOVING"); return; }
        stageMoveReported = false;''', 'prevent remote move while local stage is busy')
    r(repo,main,
      '''        stageStatus = reason + " Desired facing: " + (cue.FormationHeading * 180f / MathF.PI).ToString("0") + "°.";
        if (stageNavigator.Arrived) QueueStageReply(cue.Nonce, true, "");''',
      '''        stageFollowerTargetHeading = cue.FormationHeading;
        stageStatus = reason + " After arrival: face " +
            (cue.FormationHeading * 180f / MathF.PI).ToString("0") + "°.";
        // The ACK is delayed until the saved rotation has been confirmed.''',
      'delay follower ACK until facing')
    r(repo,main,
      '''        stageNavigator.Stop();
        capturingStage = null;
        recallingStage = null;
        stageMovementReply = null;''',
      '''        stageNavigator.Stop();
        stageFacing.Cancel();
        soloStageWalking = false;
        soloStageFacingPending = false;
        stageFollowerTargetHeading = null;
        stageFollowerFaceIssued = false;
        capturingStage = null;
        recallingStage = null;
        stageMovementReply = null;''',
      'stop pending facing with stage movement')
    r(repo,main,
      '''        stageNavigator.Stop("Conductor cancelled stage movement.");
        stageMovementReply = null;
        stageMovementNonce = "";''',
      '''        stageNavigator.Stop("Conductor cancelled stage movement.");
        stageFacing.Cancel();
        stageFollowerTargetHeading = null;
        stageFollowerFaceIssued = false;
        stageMovementReply = null;
        stageMovementNonce = "";''', 'remote stop cancels turning')
    r(repo,main,
      '''        if (Plugin.Objects.LocalPlayer is { } local)
            stageNavigator.Poll(local.Position, safe);
        if (stageCaptureReply is { } reply''',
      '''        if (Plugin.Objects.LocalPlayer is { } local)
        {
            stageNavigator.Poll(local.Position, safe);
            stageFacing.Poll(local.Position, local.Rotation, safe);
        }
        PollSoloStage(safe);
        if (stageCaptureReply is { } reply''',
      'poll nav, facing and solo')
    r(repo,main,
      '''        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
            !stageMoveReported && !string.IsNullOrEmpty(stageMovementNonce) && !stageNavigator.Active)
        {
            QueueStageReply(stageMovementNonce, stageNavigator.Arrived, stageNavigator.Arrived ? "" : "NAVFAIL");
            stageStatus = stageNavigator.Status;
        }''',
      '''        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
            !stageMoveReported && !string.IsNullOrEmpty(stageMovementNonce) && !stageNavigator.Active)
        {
            if (!stageNavigator.Arrived)
            {
                QueueStageReply(stageMovementNonce, false, "NAVFAIL");
                stageStatus = stageNavigator.Status;
            }
            else if (!cfg.AutoFaceAfterStageMovement)
            {
                QueueStageReply(stageMovementNonce, true, "");
                stageStatus = "Arrived at stage spot; auto-facing disabled for this performer.";
            }
            else if (!stageFollowerFaceIssued)
            {
                stageFollowerFaceIssued = true;
                if (stageFollowerTargetHeading is { } heading)
                    stageFacing.Start(heading);
                else
                    QueueStageReply(stageMovementNonce, false, "FACEFAIL");
            }
            else if (stageFacing.Arrived)
            {
                QueueStageReply(stageMovementNonce, true, "");
                stageStatus = "Reached stage spot and facing saved direction.";
            }
            else if (stageFacing.Failed)
            {
                QueueStageReply(stageMovementNonce, false, "FACEFAIL");
                stageStatus = stageFacing.Status;
            }
        }''',
      'follower verification and status')
    r(repo,main,
      '''            if (stageNavigator.Arrived) session.Arrived.Add(member.ContentId);''',
      '''            // Save the leader's arrival ACK until both walking and facing complete.''',
      'delay conductor arrival')
    r(repo,main,
      '''        if (!stageNavigator.Active && stageNavigator.Arrived) session.Arrived.Add(leaderId);''',
      '''        if (!stageNavigator.Active && stageNavigator.Arrived && !session.Arrived.Contains(leaderId) &&
            !session.Errors.ContainsKey(leaderId))
        {
            if (!cfg.AutoFaceAfterStageMovement)
                session.Arrived.Add(leaderId);
            else if (!session.LeaderFaceIssued)
            {
                session.LeaderFaceIssued = true;
                var leaderSpot = session.Preset.Members.First(m => m.ContentId == leaderId);
                stageFacing.Start(StageFormationMath.DesiredHeading(leaderSpot, session.Preset.AnchorHeading));
            }
            else if (stageFacing.Arrived)
                session.Arrived.Add(leaderId);
            else if (stageFacing.Failed)
                session.Errors[leaderId] = "FACEFAIL";
        }''', 'conductor auto facing')
    r(repo,main,
      '''        if (session.Pending.Count == 0 && session.Arrived.Count == session.Roster.Count)
        {
            recallingStage = null;
            stageStatus = $"Formation '{session.Preset.Name}' reached by {session.Roster.Count} Bards! " +
                "Facing is shown as a guide and is not automatically forced.";
        }''',
      '''        if (session.Pending.Count == 0 && session.Arrived.Count == session.Roster.Count)
        {
            recallingStage = null;
            stageStatus = $"Formation '{session.Preset.Name}' completed: " +
                $"{session.Roster.Count} Bards positioned (and faced where enabled).";
        }
        else if (session.Pending.Count == 0 &&
                 session.Arrived.Count + session.Errors.Count >= session.Roster.Count && session.Errors.Count > 0)
        {
            stageStatus = "Formation incomplete: " + string.Join(", ", session.Errors.Select(x =>
                session.Roster.FirstOrDefault(p => p.ContentId == x.Key)?.Name + " (" + x.Value + ")"));
            recallingStage = null;
        }''',
      'finish with facing aware status')
    r(repo,main,
      '''        ImGui.TextDisabled(Loc.T("Movement is walking only, limited to nearby spots; doesn't teleport, play music or force facing."));''',
      '''        ImGui.TextDisabled(Loc.T("Walking only, near the saved stage. No teleport and no automatic music start."));
        DrawSoloStage();''', 'solo mode UI')
    r(repo,main,
      '''            { cfg.AllowLeaderStageMovement = permit; cfg.Save(); }
            ImGui.TextDisabled(Loc.T("One-time opt-in. Disable it any time; no movement occurs on login or song load."));''',
      '''            { cfg.AllowLeaderStageMovement = permit; cfg.Save(); }
            bool face = cfg.AutoFaceAfterStageMovement;
            if (ImGui.Checkbox(Loc.T("Automatically face saved direction after walking##stageAutoFace"), ref face))
            { cfg.AutoFaceAfterStageMovement = face; cfg.Save(); }
            ImGui.TextDisabled(Loc.T("Explicit opt-in for movement; no movement occurs on login or song load."));''',
      'performer facing toggle')
    r(repo,main,
      '''            if (ImGui.Button(Loc.T("SAVE CURRENT PARTY POSITIONS"))) SaveStageFormation();
            ImGui.TextDisabled(Loc.T("Position everyone manually once, then Save. All party members must opt in."));''',
      '''            if (ImGui.Button(Loc.T("SAVE CURRENT PARTY POSITIONS"))) SaveStageFormation();
            ImGui.TextDisabled(Loc.T("Position everyone manually once, then Save. All party members must opt in."));
            bool face = cfg.AutoFaceAfterStageMovement;
            if (ImGui.Checkbox(Loc.T("Face my conductor when stage movement ends##stageLeaderAutoFace"), ref face))
            { cfg.AutoFaceAfterStageMovement = face; cfg.Save(); }''',
      'conductor local-facing toggle')
    r(repo,main,
      'ImGui.TextDisabled(Loc.T("This saves relative positions and a facing guide. Automatic facing is not yet implemented."));',
      'ImGui.TextDisabled(Loc.T("Saved facing directions are applied after walking on clients with auto-facing enabled."));',
      'update stage caption')
    r(repo,main,
      '''        stageNavigator.Stop("Stage feature reset after an error: " + detail);
        capturingStage = null;''',
      '''        stageNavigator.Stop("Stage feature reset after an error: " + detail);
        stageFacing.Cancel();
        soloStageWalking = false;
        soloStageFacingPending = false;
        stageFollowerTargetHeading = null;
        stageFollowerFaceIssued = false;
        capturingStage = null;''',
      'safety recovery cancels facing')
    r(repo,main,
      '''        if (stageNavigator.Active || recallingStage is not null ||
            !string.IsNullOrEmpty(stageMovementNonce))''',
      '''        if (stageNavigator.Active || stageFacing.Active || soloStageWalking ||
            soloStageFacingPending || recallingStage is not null ||
            !string.IsNullOrEmpty(stageMovementNonce))''',
      'music stops solo movement and turning')
    r(repo,main,
      '''    internal void StopStageMovementOnUnload() => stageNavigator.Stop("Plugin unloaded; stage movement stopped.");''',
      '''    internal void StopStageMovementOnUnload()
    {
        stageNavigator.Stop("Plugin unloaded; stage movement stopped.");
        stageFacing.Cancel();
        soloStageWalking = false;
        soloStageFacingPending = false;
    }''',
      'plugin unload safety')

    r(repo,designer,
      '''        if (ImGui.SmallButton(Loc.T("Circle##visualShapeCircle"))) draft.ApplyTemplate(VisualStageTemplate.Circle);

        DrawVisualStageCanvas(draft);''',
      '''        if (ImGui.SmallButton(Loc.T("Circle##visualShapeCircle"))) draft.ApplyTemplate(VisualStageTemplate.Circle);
        if (ImGui.SmallButton(Loc.T("ALL FACE AUDIENCE##visualFaceAll")))
            for (int i = 0; i < draft.Members.Count; i++) draft.SetHeading(i, 0f);

        DrawVisualStageCanvas(draft);''',
      'conductor all face forward button')
    r(repo,designer,
      '''            ImGui.TextDisabled(Loc.T("Facing is a visual guide only; vnavmesh walks to the selected dot."));''',
      '''            ImGui.TextDisabled(Loc.T("Facing is applied on arrival if the performer has opted in."));''',
      'designer facing is now active')
    r(repo,designer,
      '''            draw.AddRectFilled(xy - new Vector2(radius, radius), xy + new Vector2(radius, radius), colour, radius);
            draw.AddText(xy + new Vector2(i + 1 >= 10 ? -8f : -4f, -7f),''',
      '''            draw.AddRectFilled(xy - new Vector2(radius, radius), xy + new Vector2(radius, radius), colour, radius);
            Vector2 aim = new(MathF.Sin(member.HeadingOffset), MathF.Cos(member.HeadingOffset));
            draw.AddLine(xy, xy + aim * 21f, border, 3f);
            draw.AddText(xy + new Vector2(i + 1 >= 10 ? -8f : -4f, -7f),''',
      'draw per-member facing arrows')


def main():
    parser = argparse.ArgumentParser(description='Patch a local NunuWindboundBard v0.34 source tree.')
    parser.add_argument('repo', type=Path, help='Path to the existing repository root (with src/ inside)')
    args = parser.parse_args()
    repo = args.repo.expanduser().resolve()
    if not (repo / 'src/NunuWindboundBard/Plugin.cs').is_file():
        raise PatchError(f'Not a Windbound Bard source root: {repo}')
    stage(repo)  # Performs ALL preflight checks before writing.

    source = ROOT / 'new-files'
    newcomers = sorted(p for p in source.rglob('*') if p.is_file())
    for file in newcomers:
        dest = repo / file.relative_to(source)
        if dest.exists():
            raise PatchError(f'New file already exists: {dest} (no files changed).')

    stamp = datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
    backup = repo / '.windbound-stage-backups' / stamp
    backup.mkdir(parents=True, exist_ok=False)
    for path in sorted(changes):
        original = repo / path
        saved = backup / path
        saved.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, saved)
    for path, content in changes.items():
        (repo / path).write_text(content, encoding='utf-8')
    for file in newcomers:
        dest = repo / file.relative_to(source)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, dest)

    print(f'PATCHED {len(changes)} source files; added {len(newcomers)} new C# files.')
    print('Backup: ' + str(backup))
    print('Next: dotnet build src/NunuWindboundBard/NunuWindboundBard.csproj -c Release')
    print('This environment did not compile the plugin; validate on your Dalamud SDK installation.')

if __name__ == '__main__':
    try:
        main()
    except PatchError as exc:
        print('PATCH NOT APPLIED: ' + str(exc), file=sys.stderr)
        sys.exit(1)
