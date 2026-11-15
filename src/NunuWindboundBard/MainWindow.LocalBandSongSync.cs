using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private sealed record LocalSongSyncContext(EnsembleRole Role, string Stage, string PlaylistId);
    private sealed record LocalSongSyncResult(
        LocalBandSongSync.Publication? Published, LocalBandSongSync.Reception? Received);

    private LocalSongSyncContext? localSongSyncContext;
    private Task<LocalSongSyncResult>? localSongSyncTask;
    private CancellationTokenSource? localSongSyncCancellation;
    private string[] localSongSyncWorkingPlaylist = [];
    private string[]? localSongSyncPublishedPlaylist;
    private Dictionary<string, LocalBandSongSync.PublishedFile> localSongSyncSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> localSongSyncCuePaths = new(StringComparer.OrdinalIgnoreCase);
    private Guid localSongSyncRevision;
    private long nextLocalSongSyncPoll;
    private bool forceLocalSongSync;
    private string localSongSyncStatus = "";

    private void DrawLocalBandSongSync(EnsembleRole role)
    {
        if (role == EnsembleRole.Solo) return;
        bool enabled = cfg.LocalSongSyncEnabled;
        if (ImGui.Checkbox(Loc.T("Sync songs on this PC"), ref enabled))
        {
            cfg.LocalSongSyncEnabled = enabled;
            cfg.Save();
        }
        if (!enabled) return;

        ImGui.TextWrapped(Loc.T(role == EnsembleRole.Conductor
            ? "Copies songs from the active Ensemble playlist for followers using the same stage and Windows user account."
            : "Adds shared MIDI files to the active Ensemble playlist. Use the same stage as the conductor."));
        bool busy = localSongSyncTask is { IsCompleted: false };
        if (busy) ImGui.BeginDisabled();
        if (ImGui.SmallButton(Loc.T(role == EnsembleRole.Conductor ? "Share playlist now" : "Check for shared songs")))
        {
            forceLocalSongSync = true;
            nextLocalSongSyncPoll = 0;
        }
        if (busy) ImGui.EndDisabled();
        if (!string.IsNullOrEmpty(localSongSyncStatus))
            ImGui.TextWrapped(Loc.T(localSongSyncStatus));
    }

    // All disk copying and hashing run in the background. Only this framework
    // update can append to the active playlist or touch the plugin configuration.
    private void PollLocalBandSongSync()
    {
        var role = (EnsembleRole)cfg.EnsembleMode;
        var context = cfg.LocalSongSyncEnabled && role is EnsembleRole.Conductor or EnsembleRole.Follower
            ? new LocalSongSyncContext(role, cfg.EnsembleGroup?.Trim().ToLowerInvariant() ?? "", cfg.ActivePlaylistId)
            : null;
        if (context != localSongSyncContext)
        {
            localSongSyncCancellation?.Cancel();
            localSongSyncContext = context;
            localSongSyncPublishedPlaylist = null;
            localSongSyncSources = new(StringComparer.OrdinalIgnoreCase);
            localSongSyncCuePaths.Clear();
            localSongSyncRevision = Guid.Empty;
            localSongSyncStatus = context?.Role == EnsembleRole.Follower ? "Waiting for the conductor to share songs." : "";
            nextLocalSongSyncPoll = 0;
        }
        if (localSongSyncTask is { } work)
        {
            if (!work.IsCompleted) return;
            bool cancelled = localSongSyncCancellation!.IsCancellationRequested;
            localSongSyncTask = null;
            localSongSyncCancellation.Dispose();
            localSongSyncCancellation = null;
            try
            {
                var result = work.GetAwaiter().GetResult();
                if (!cancelled && context is not null)
                    ApplyLocalBandSongSync(result);
            }
            catch (Exception ex)
            {
                if (!cancelled)
                {
                    localSongSyncStatus = "Song sync: " + ex.Message;
                    nextLocalSongSyncPoll = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
                }
            }
        }
        if (context is null || Stopwatch.GetTimestamp() < nextLocalSongSyncPoll) return;
        nextLocalSongSyncPoll = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        try
        {
            if (context.Role == EnsembleRole.Conductor && !forceLocalSongSync &&
                localSongSyncPublishedPlaylist is not null &&
                cfg.Playlist.SequenceEqual(localSongSyncPublishedPlaylist, StringComparer.OrdinalIgnoreCase))
                return;

            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(localData)) throw new IOException("Local AppData is unavailable.");
            string directory = LocalBandSongSync.GetStageDirectory(
                Path.Combine(localData, "NunuWindboundBard", "BandSongs"), context.Stage);
            string[] playlist = cfg.Playlist.ToArray();
            localSongSyncWorkingPlaylist = playlist;
            var sources = forceLocalSongSync ? new Dictionary<string, LocalBandSongSync.PublishedFile>() : localSongSyncSources;
            Guid revision = forceLocalSongSync ? Guid.Empty : localSongSyncRevision;
            string[] receipts = context.Role == EnsembleRole.Follower
                ? SavedPlaylistLibrary.GetActive(cfg).LocalSongSyncHashes.ToArray() : [];
            forceLocalSongSync = false;
            localSongSyncCancellation = new CancellationTokenSource();
            var token = localSongSyncCancellation.Token;
            localSongSyncTask = Task.Run(() => context.Role == EnsembleRole.Conductor
                ? new LocalSongSyncResult(LocalBandSongSync.Publish(directory, playlist, sources, token), null)
                : new LocalSongSyncResult(null, LocalBandSongSync.Receive(directory, revision, receipts, playlist, token)), token);
        }
        catch (Exception ex) { localSongSyncStatus = "Song sync: " + ex.Message; }
    }

    private void ApplyLocalBandSongSync(LocalSongSyncResult result)
    {
        if (result.Published is { } publication)
        {
            localSongSyncSources = publication.Sources;
            localSongSyncPublishedPlaylist = localSongSyncWorkingPlaylist;
            localSongSyncStatus = $"Sharing {publication.Count} MIDI(s) from this playlist.";
            if (publication.Skipped > 0)
                localSongSyncStatus += $" Skipped {publication.Skipped}. {publication.Error}";
            return;
        }
        if (result.Received is not { } incoming ||
            !cfg.Playlist.SequenceEqual(localSongSyncWorkingPlaylist, StringComparer.OrdinalIgnoreCase))
            return;

        var profile = SavedPlaylistLibrary.GetActive(cfg);
        int previousCount = cfg.Playlist.Count;
        int previousReceipts = profile.LocalSongSyncHashes.Count;
        foreach (var entry in incoming.Songs)
        {
            if (!entry.AlreadyListed) cfg.Playlist.Add(entry.Path);
            profile.LocalSongSyncHashes.Add(entry.Song.Hash);
        }
        if (incoming.Songs.Count > 0)
        {
            try { PersistActivePlaylist(); }
            catch
            {
                cfg.Playlist.RemoveRange(previousCount, cfg.Playlist.Count - previousCount);
                profile.LocalSongSyncHashes.RemoveRange(previousReceipts, profile.LocalSongSyncHashes.Count - previousReceipts);
                SavedPlaylistLibrary.SnapshotActive(cfg);
                throw;
            }
            foreach (var entry in incoming.Songs)
                localSongSyncCuePaths[entry.Song.Hash[..16]] = entry.Path;
            if (activePlaylist < 0 && !string.IsNullOrEmpty(loaded))
                activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, loaded, StringComparison.OrdinalIgnoreCase));
            localSongSyncStatus = $"Added {cfg.Playlist.Count - previousCount} shared MIDI(s) to {profile.Name}.";
        }
        if (incoming.Unavailable > 0)
        {
            localSongSyncStatus = $"Waiting for {incoming.Unavailable} shared MIDI(s). Ask the conductor to share the playlist again.";
            return;
        }
        localSongSyncRevision = incoming.Revision;
        if (incoming.Songs.Count == 0)
            localSongSyncStatus = "No new shared songs for this playlist.";
    }

    private void DisposeLocalBandSongSync()
    {
        localSongSyncCancellation?.Cancel();
        localSongSyncCancellation?.Dispose();
        if (localSongSyncTask is { } work)
            _ = work.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
