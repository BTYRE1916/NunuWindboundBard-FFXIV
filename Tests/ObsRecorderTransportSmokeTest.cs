using System;
using NunuWindboundBard.Core;

static void Check(bool value, string message)
{
    if (!value) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

var offline = new ObsCaptureSnapshot();
Check(!ObsRecordTransport.CanStart(offline) && !ObsRecordTransport.CanPause(offline) &&
      !ObsRecordTransport.CanResume(offline) && !ObsRecordTransport.CanStop(offline), "no commands when offline");
var ready = offline with { Connected = true };
Check(ObsRecordTransport.CanStart(ready) && !ObsRecordTransport.CanPause(ready) &&
      !ObsRecordTransport.CanResume(ready) && !ObsRecordTransport.CanStop(ready), "start only when standby");
var recording = ObsRecordingClient.SnapshotAfterAcceptedCommand(ready, "StartRecord");
Check(recording.Recording && !recording.Paused && ObsRecordTransport.CanPause(recording) &&
      ObsRecordTransport.CanStop(recording), "start opens recording transport");
var paused = ObsRecordingClient.SnapshotAfterAcceptedCommand(recording, "PauseRecord");
Check(paused.Recording && paused.Paused && ObsRecordTransport.CanResume(paused) &&
      !ObsRecordTransport.CanPause(paused), "paused recording can resume");
var resumed = ObsRecordingClient.SnapshotAfterAcceptedCommand(paused, "ResumeRecord");
Check(resumed.Recording && !resumed.Paused && ObsRecordTransport.CanPause(resumed),
      "resume continues recording");
var stopped = ObsRecordingClient.SnapshotAfterAcceptedCommand(resumed, "StopRecord");
Check(!stopped.Recording && !stopped.Paused && ObsRecordTransport.CanStart(stopped),
      "stop returns to standby");
var busy = recording with { Busy = true };
Check(!ObsRecordTransport.CanStart(busy) && !ObsRecordTransport.CanPause(busy) &&
      !ObsRecordTransport.CanResume(busy) && !ObsRecordTransport.CanStop(busy), "busy state blocks duplicate commands");
Check(ObsRecordingClient.SnapshotAfterAcceptedCommand(ready, "PauseRecord") == ready,
      "invalid pause cannot change idle snapshot");
Check(ObsRecordingClient.FormatDuration(51 * 3600_000L) == "51:00:00", "recording timer remains unlimited");
Console.WriteLine("OBS recording transport smoke test passed. Live OBS test is still required.");
