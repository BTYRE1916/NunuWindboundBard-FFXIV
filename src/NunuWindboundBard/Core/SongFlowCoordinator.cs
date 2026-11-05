using System;

namespace NunuWindboundBard.Core;

public enum SongFlowActionKind { None, Next, SelectSong, StartLoaded }
public readonly record struct SongFlowAction(SongFlowActionKind Kind, string Path = "", bool PlayAfterLoad = false);

/// <summary>
/// One-shot song transitions, driven by Dalamud Framework.Update rather than
/// ImGui Draw. No focus check. Manual stop cancels queued song changes.
/// </summary>
public sealed class SongFlowCoordinator
{
    private SongFlowAction pending;
    private bool observedPlaying;
    public bool HasPending => pending.Kind != SongFlowActionKind.None;
    public string PendingDescription => pending.Kind switch
    {
        SongFlowActionKind.Next => "Next song after stop",
        SongFlowActionKind.SelectSong => $"Load {pending.Path} (autoplay: {pending.PlayAfterLoad})",
        SongFlowActionKind.StartLoaded => "Play loaded song after stop",
        _ => "None"
    };

    public void QueueNext() => pending = new(SongFlowActionKind.Next);
    public void QueueSelection(string path, bool startAfterLoad)
        => pending = new(SongFlowActionKind.SelectSong, path, startAfterLoad);
    public void QueueStartLoaded()
    {
        if (!HasPending) pending = new(SongFlowActionKind.StartLoaded);
    }
    public void MarkStarted() => observedPlaying = true;
    public void Cancel() { pending = default; observedPlaying = false; }

    public SongFlowAction Poll(bool isPlaying, bool completedSuccessfully, bool autoNext, bool solo)
    {
        if (isPlaying)
        {
            observedPlaying = true;
            return default;
        }
        if (HasPending)
        {
            var queued = pending;
            pending = default;
            observedPlaying = false;
            return queued;
        }
        if (observedPlaying && completedSuccessfully && autoNext && solo)
        {
            observedPlaying = false;
            return new(SongFlowActionKind.Next);
        }
        observedPlaying = false;
        return default;
    }
}
