using System;

namespace NunuWindboundBard.Core;

/// <summary>
/// OBS recorder transport guards shared by the Concert Capture window and HUD.
/// A disabled button remains visible, so users can find Pause/Resume before
/// recording starts, while commands cannot run in an invalid recorder state.
/// </summary>
public static class ObsRecordTransport
{
    public static bool CanStart(ObsCaptureSnapshot state) =>
        state.Connected && !state.Busy && !state.Recording;

    public static bool CanPause(ObsCaptureSnapshot state) =>
        state.Connected && !state.Busy && state.Recording && !state.Paused;

    public static bool CanResume(ObsCaptureSnapshot state) =>
        state.Connected && !state.Busy && state.Recording && state.Paused;

    public static bool CanStop(ObsCaptureSnapshot state) =>
        state.Connected && !state.Busy && state.Recording;

    public static string StatusLabel(ObsCaptureSnapshot state) =>
        !state.Connected ? "OFFLINE — CONNECT OBS" :
        state.Recording ? (state.Paused ? "RECORDING PAUSED" : "RECORDING LIVE") :
        state.Busy ? "OBS WORKING..." : "STANDBY — READY TO RECORD";
}
