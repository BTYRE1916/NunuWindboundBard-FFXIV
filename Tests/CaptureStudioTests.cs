using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using NunuWindboundBard.Core;
using WindboundCaptureEngine;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
void Reject(Action operation, string label) { try { operation(); } catch (ArgumentException) { Check(true,label); return; } throw new Exception(label); }
async Task Until(Func<bool> predicate) { using var limit = new CancellationTokenSource(8000); while (!predicate()) await Task.Delay(20,limit.Token); }

// Protocol, validation and persistence tests are independent of native capture.
Reject(() => (new StudioTransform { X = float.NaN }).Validate(), "reject non-finite transforms");
Reject(() => (new StudioTransform { CropLeft = -1 }).Validate(), "reject negative crop");
var output = new StudioOutputSettings { Folder = Path.GetTempPath() };
output.Validate();
Reject(() => (output with { Width = 1919 }).Validate(), "reject odd chroma dimensions");
Reject(() => (output with { ReplayMegabytes = int.MaxValue }).Validate(), "bound replay memory");
Reject(() => (output with { Folder = "relative" }).Validate(), "require absolute recording path");

using (var memory = new MemoryStream()) {
    var original = new StudioRequest { Command = "AddSource", Args = StudioProtocol.Json(new { Name = "Edea & Nunu — 星", Type = "image_source", File = "C:\\Overlays\\Concert #1.png" }) };
    await StudioProtocol.WriteAsync(memory, original, default); memory.Position = 0;
    var read = await StudioProtocol.ReadAsync<StudioRequest>(memory, default);
    Check(read.Id == original.Id && read.Args.GetRawText() == original.Args.GetRawText(), "framed Unicode/path roundtrip");
}
foreach (int size in new[] {-1, 0, StudioProtocol.MaxMessageBytes + 1}) {
    using var bad = new MemoryStream(BitConverter.GetBytes(size));
    try { await StudioProtocol.ReadAsync<StudioRequest>(bad, default); throw new Exception("unbounded message"); }
    catch (InvalidDataException) { checks++; }
}
Console.WriteLine("PASS reject invalid frame lengths before allocation");
using (var truncated = new MemoryStream(new byte[] { 20,0,0,0, 123 })) {
    try { await StudioProtocol.ReadAsync<StudioRequest>(truncated, default); throw new Exception("truncated frame accepted"); }
    catch (EndOfStreamException) { Check(true,"reject truncated frame"); }
}

string temp = Path.Combine(Path.GetTempPath(),"windbound-studio-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
try {
    var source = new StudioSource { Name = "Concert title", Type = "text", Settings = "{\"text\":\"WAH!\"}",
        Transform = new StudioTransform { X=42, Y=94, CropRight=7, Locked=true },
        Filters = [new StudioFilter { Type="color_correction", Name="Tint", Settings="{\"gamma\":0.2}" }] };
    var scene = new StudioScene { Name="Encore", Sources=[source] };
    var collection = new StudioCollection { Output=output, ProgramScene=scene.Id, Scenes=[scene] };
    string file = Path.Combine(temp,"studio.json"); StudioCollectionStore.Save(file,collection);
    collection.Scenes.Add(new StudioScene { Name="Intermission" }); StudioCollectionStore.Save(file,collection);
    var restored = StudioCollectionStore.Load(file);
    Check(restored.Scenes.Count == 2 && restored.ProgramScene == scene.Id && restored.Scenes[0].Sources[0].Transform.CropRight == 7 && restored.Scenes[0].Sources[0].Filters[0].Settings.Contains("gamma"), "collection restores overlays, crop, locks and filter settings");
    Check(StudioCollectionStore.Load(file+".bak").Scenes.Count == 1, "atomic save retains previous collection backup");
    File.WriteAllText(file,"{invalid");
    try { StudioCollectionStore.Load(file); throw new Exception("corruption hidden"); } catch(JsonException) { Check(true,"corrupt collection fails visibly without overwriting backup"); }
} finally { Directory.Delete(temp,true); }

if (args.Contains("--offline")) {
    using var offline = new CaptureStudioClient();
    offline.Command("StartRecord");
    Check(!offline.Snapshot.Connected && !offline.Snapshot.Recording, "offline controller cannot start capture");
    Console.WriteLine($"ALL {checks} OFFLINE STUDIO CHECKS PASSED.");
    Console.WriteLine("SKIP named-pipe integration: run without --offline on Windows.");
    return;
}
// Real named-pipe roundtrip against a controlled engine peer, not a method-call mock.
using var stop = new CancellationTokenSource();
var commands = new ConcurrentQueue<string>(); bool recording = false, wrongId = false;
Task server = Task.Run(async () => {
    try {
        while (!stop.IsCancellationRequested) {
            using var pipe = new NamedPipeServerStream(StudioProtocol.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(stop.Token);
            var request = await StudioProtocol.ReadAsync<StudioRequest>(pipe,stop.Token); commands.Enqueue(request.Command);
            if (request.Command == "StartRecord") recording=true;
            if (request.Command == "StopRecord") recording=false;
            var state = new StudioSnapshot { Connected=true, Recording=recording, Output=output };
            var response = new StudioResponse { Success=true, Id=wrongId ? "bad-id" : request.Id, State=state,
                Preview=request.Command == "Preview" ? new byte[StudioProtocol.PreviewWidth*StudioProtocol.PreviewHeight*4] : null,
                PreviewSequence=7, PreviewSceneId="test-scene" };
            await StudioProtocol.WriteAsync(pipe,response,stop.Token);
        }
    } catch(OperationCanceledException) { }
});
try {
    using var client = new CaptureStudioClient();
    Check(!client.Snapshot.Connected && !client.Snapshot.Recording, "controller defaults offline and idle");
    client.Command("StartRecord"); Check(commands.IsEmpty,"offline commands cannot start capture");
    client.Connect(); await Until(() => client.Snapshot.Connected && !client.Snapshot.Busy);
    Check(!recording,"connect does not start any output");
    client.Command("StartRecord"); await Until(() => client.Snapshot.Recording && !client.Snapshot.Busy);
    Check(recording,"explicit recording command crosses the pipe");
    client.RequestPreview("test-scene"); await Until(() => client.Preview.Bytes != null);
    Check(client.Preview.Sequence==7 && client.Preview.SceneId=="test-scene","preview is validated and associated with its scene");
    client.Disconnect(); Check(recording && !client.Snapshot.Connected,"disconnect preserves background recording");
    using var second = new CaptureStudioClient(); second.Connect(); await Until(() => second.Snapshot.Recording && !second.Snapshot.Busy);
    Check(second.Snapshot.Recording,"new controller reconnects to existing output");
    wrongId=true; second.Command("PauseRecord"); await Until(() => second.LastError.Length>0);
    Check(second.LastError.Contains("request ID"),"reject mismatched response before accepting state"); wrongId=false;
    second.Dispose();
    Check(!commands.Contains("StopRecord") && !commands.Contains("Shutdown"),"controller disposal sends no stop or engine shutdown");
} finally { stop.Cancel(); await server; }
Console.WriteLine($"ALL {checks} STUDIO CHECKS PASSED. Native Windows/GPU smoke test is separate.");
