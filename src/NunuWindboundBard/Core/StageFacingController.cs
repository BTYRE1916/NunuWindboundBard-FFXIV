using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.Config;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NunuWindboundBard.Core;

/// <summary>
/// Requests facing through the game's existing AutoFaceTargetPosition behaviour.
/// No actor-position or rotation fields are written. The caller must invoke this
/// on the Dalamud framework thread, after walking has stopped and before playing.
/// Each request is bounded, and actual rotation is checked before reporting success.
/// </summary>
public sealed unsafe class StageFacingController
{
    private float requestedHeading;
    private long startedQpc;
    private long lastAttemptQpc;

    public bool Active { get; private set; }
    public bool Arrived { get; private set; }
    public bool Failed { get; private set; }
    public string Status { get; private set; } = "No stage facing requested.";

    public void Start(float heading)
    {
        if (!float.IsFinite(heading))
            throw new ArgumentOutOfRangeException(nameof(heading));
        Cancel();
        requestedHeading = StageFormationMath.WrapHeading(heading);
        startedQpc = Stopwatch.GetTimestamp();
        Active = true;
        Status = "Turning to the chosen stage direction...";
    }

    public void Poll(Vector3 position, float actualHeading, bool safe)
    {
        if (!Active) return;
        if (!safe || !StageFormationMath.Finite(position) || !float.IsFinite(actualHeading))
        {
            Fail("Facing cancelled: character cannot safely turn now.");
            return;
        }
        if (MathF.Abs(StageFormationMath.WrapHeading(actualHeading - requestedHeading)) <= 0.13f)
        {
            Active = false;
            Arrived = true;
            Status = "Character faces the saved stage direction.";
            return;
        }
        double elapsed = (Stopwatch.GetTimestamp() - startedQpc) / (double)Stopwatch.Frequency;
        if (elapsed > 3.0)
        {
            Fail("Automatic facing timed out. Turn manually or disable auto-facing for this client.");
            return;
        }
        if (lastAttemptQpc != 0 &&
            (Stopwatch.GetTimestamp() - lastAttemptQpc) / (double)Stopwatch.Frequency < 0.3)
            return;
        lastAttemptQpc = Stopwatch.GetTimestamp();
        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
            {
                Fail("FFXIV ActionManager is not ready.");
                return;
            }
            // FFXIV character rotation is atan2(X, Z): 0 means +Z.
            Vector3 lookAt = position + new Vector3(MathF.Sin(requestedHeading), 0, MathF.Cos(requestedHeading)) * 3f;
            // Respect the player's existing setting: restore it immediately after
            // the native facing request, even if the request throws.
            if (!Plugin.GameConfig.TryGet(UiControlOption.AutoFaceTargetOnAction, out uint original))
            {
                Fail("Could not read FFXIV's Auto-face Target setting.");
                return;
            }
            try
            {
                Plugin.GameConfig.Set(UiControlOption.AutoFaceTargetOnAction, 1u);
                actionManager->AutoFaceTargetPosition(&lookAt);
            }
            finally
            {
                Plugin.GameConfig.Set(UiControlOption.AutoFaceTargetOnAction, original);
            }
        }
        catch (Exception ex)
        {
            Fail("Automatic facing is unavailable on this client: " + ex.Message);
        }
    }

    public void Cancel()
    {
        Active = false;
        Arrived = false;
        Failed = false;
        startedQpc = 0;
        lastAttemptQpc = 0;
        Status = "Stage facing cancelled.";
    }

    private void Fail(string message)
    {
        Active = false;
        Arrived = false;
        Failed = true;
        Status = message;
    }
}
