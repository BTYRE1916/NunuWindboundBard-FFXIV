using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace NunuWindboundBard.Core;

/// <summary>
/// Optional vnavmesh IPC movement. We never write a character's coordinates to
/// game memory or synthesize input. If navmesh is absent, movement is unavailable.
/// Call Start and Poll on Dalamud's framework thread only.
/// </summary>
public sealed class StageFormationNavigator
{
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<Vector3, bool, Task<bool>> pathfind;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<bool> pathfinding;
    private readonly ICallGateSubscriber<object> stop;
    private Task<bool>? request;
    private Vector3 destination;
    private long started;
    public bool Active { get; private set; }
    public bool Arrived { get; private set; }
    public string Status { get; private set; } = "No stage movement running.";

    public StageFormationNavigator(IDalamudPluginInterface plugin)
    {
        navReady = plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfind = plugin.GetIpcSubscriber<Vector3, bool, Task<bool>>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathRunning = plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfinding = plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        stop = plugin.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsAvailable => navReady.HasFunction && pathfind.HasFunction;
    public bool TryStart(Vector3 here, Vector3 target, out string reason)
    {
        Arrived = false;
        if (Active) { reason = "Another stage movement is already running."; return false; }
        if (!StageFormationMath.Finite(here) || !StageFormationMath.Finite(target))
        { reason = "Invalid stage coordinates."; return false; }
        float distance = Vector3.Distance(here, target);
        if (distance > StageFormationMath.MaxMoveDistance)
        { reason = "Stage destination is more than 40m away. Approach the saved stage first."; return false; }
        if (distance <= StageFormationMath.ArrivalTolerance)
        { Arrived = true; Status = reason = "Already at saved stage position."; return true; }
        try
        {
            if (!IsAvailable || !navReady.InvokeFunc())
            { reason = "vnavmesh is missing or its navigation mesh is not ready on this client."; return false; }
            if ((pathRunning.HasFunction && pathRunning.InvokeFunc()) ||
                (pathfinding.HasFunction && pathfinding.InvokeFunc()))
            { reason = "Another vnavmesh movement is active; stop it before recalling the stage."; return false; }
            request = pathfind.InvokeFunc(target, false); // walking only, no flying/teleporting
            destination = target;
            started = Stopwatch.GetTimestamp();
            Active = true;
            Status = reason = "Walking to the saved stage position using vnavmesh...";
            return true;
        }
        catch (Exception ex) { reason = "Navigation request failed: " + ex.Message; Status = reason; return false; }
    }

    public void Poll(Vector3 here, bool safe)
    {
        if (!Active) return;
        try
        {
            if (!safe || !IsAvailable)
            { Stop("Stopped: game state changed or navigation plugin is unavailable."); return; }
            if (Vector3.Distance(here, destination) <= StageFormationMath.ArrivalTolerance)
            { Active = false; Arrived = true; Status = "Arrived at saved stage position."; request = null; return; }
            if (request is { IsCompleted: true } && (request.IsFaulted || request.IsCanceled || !request.Result))
            { Stop("vnavmesh could not calculate a walking path to the saved spot."); return; }
            double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            if (seconds > 50) Stop("Timed out walking to the saved stage position.");
        }
        catch (Exception ex) { Stop("Movement failed: " + ex.Message); }
    }

    public void Stop(string message = "Stage movement cancelled.")
    {
        if (Active && stop.HasAction)
        {
            try { stop.InvokeAction(); }
            catch { /* The IPC provider may disappear during plugin reload. */ }
        }
        Active = false;
        Arrived = false;
        request = null;
        Status = message;
    }
}
