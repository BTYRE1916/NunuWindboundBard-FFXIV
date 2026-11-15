using NunuWindboundBard.Core;

static void Assert(bool predicate, string message)
{
    if (!predicate) throw new Exception("Game ensemble start test failed: " + message);
}

const long freq = 10_000_000;
const long initial = 20 * freq;
var gate = new GameEnsembleStartGate();
gate.Capture(new GameEnsembleStartSignal(1, initial));
var first = gate.Evaluate(GameEnsembleReadiness.TemporaryBlock, "Performance window settling", initial + freq/4, freq);
Assert(first.Kind == GameEnsembleDecisionKind.Waiting, "temporary readiness waits for window settling");
var playable = gate.Evaluate(GameEnsembleReadiness.Ready, "ready", initial + freq/2, freq);
Assert(playable.Kind == GameEnsembleDecisionKind.Play && playable.Late && playable.StartQpc == initial + freq/2,
       "late but recoverable native start is played once from the recovered time");
Assert(!gate.HasPending, "successful start is consumed exactly once");
Assert(gate.Evaluate(GameEnsembleReadiness.Ready, "ready", initial + freq/2, freq).Kind == GameEnsembleDecisionKind.NoSignal,
       "consumed start is not replayed");

gate.Capture(new GameEnsembleStartSignal(1, initial + freq));
Assert(!gate.HasPending, "duplicate native signals are not replayed");

gate.Capture(new GameEnsembleStartSignal(2, initial + freq));
var blocked = gate.Evaluate(GameEnsembleReadiness.PermanentBlock, "output not ARMed", initial + freq, freq);
Assert(blocked.Kind == GameEnsembleDecisionKind.Failed && blocked.Reason.Contains("ARMed"),
       "permanent failure is reported immediately");

gate.Capture(new GameEnsembleStartSignal(3, initial + 2*freq));
var expired = gate.Evaluate(GameEnsembleReadiness.TemporaryBlock, "old song stopping",
    initial + 2*freq + 2*freq, freq);
Assert(expired.Kind == GameEnsembleDecisionKind.Failed, "readiness grace period is bounded");

gate.Capture(new GameEnsembleStartSignal(4, initial + 5*freq));
var silent = gate.Evaluate(GameEnsembleReadiness.Silent, "silent conductor", initial + 5*freq, freq);
Assert(silent.Kind == GameEnsembleDecisionKind.Silent, "unassigned conductor stays silent");

gate.Capture(new GameEnsembleStartSignal(5, initial + 6*freq));
gate.Cancel("mode changed");
Assert(!gate.HasPending, "mode change cancels stale native start");
Console.WriteLine("PASS: ready, transient, timeout, duplicate, permanently blocked and silent ensemble starts.");
