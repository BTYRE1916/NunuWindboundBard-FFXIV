using NunuWindboundBard.Core;

static class Tests
{
    static int count;
    static void Check(bool test, string why)
    {
        if (!test) throw new Exception("FAILED: " + why);
        count++;
    }

    static void AutoNextWithHiddenWindow()
    {
        var f = new SongFlowCoordinator();
        f.MarkStarted();
        Check(f.Poll(true, false, true, true).Kind == SongFlowActionKind.None, "stay while playing");
        Check(f.Poll(false, true, true, true).Kind == SongFlowActionKind.Next, "advance after natural finish");
        Check(f.Poll(false, true, true, true).Kind == SongFlowActionKind.None, "advance at most once");
        f.MarkStarted();
        Check(f.Poll(false, true, false, true).Kind == SongFlowActionKind.None, "Auto Next disabled");
        f.MarkStarted();
        Check(f.Poll(false, true, true, false).Kind == SongFlowActionKind.None, "no ensemble auto advance");
    }

    static void StopsAndManualSelection()
    {
        var f = new SongFlowCoordinator();
        f.MarkStarted();
        f.QueueSelection(@"C:\\Songs\\new.mid", true);
        Check(f.Poll(true, false, true, true).Kind == SongFlowActionKind.None, "wait for old note releases");
        var select = f.Poll(false, false, true, true);
        Check(select.Kind == SongFlowActionKind.SelectSong && select.PlayAfterLoad &&
            select.Path.EndsWith("new.mid"), "resume manual selection");
        Check(f.Poll(false, false, true, true).Kind == SongFlowActionKind.None, "manual select only once");
        f.MarkStarted();
        f.QueueNext();
        Check(f.Poll(true, false, false, true).Kind == SongFlowActionKind.None, "Next waits for note releases");
        Check(f.Poll(false, false, false, true).Kind == SongFlowActionKind.Next, "manual Next advances even without Auto Next");
        f.MarkStarted();
        f.QueueStartLoaded();
        f.Cancel();
        Check(f.Poll(false, true, true, true).Kind == SongFlowActionKind.None, "STOP cancels pending and auto advance");
    }

    static void PostStopAndEnsemble()
    {
        var f = new SongFlowCoordinator();
        f.MarkStarted();
        f.QueueStartLoaded();
        f.QueueStartLoaded();
        Check(f.Poll(true, false, true, true).Kind == SongFlowActionKind.None, "play waits for stop completion");
        Check(f.Poll(false, false, true, true).Kind == SongFlowActionKind.StartLoaded, "deferred Play fires once");
        Check(f.Poll(false, false, true, true).Kind == SongFlowActionKind.None, "no duplicate deferred Play");
        f.MarkStarted();
        f.QueueSelection("duet.mid", false);
        Check(!f.Poll(false, false, true, false).PlayAfterLoad, "ensemble selection never auto-cues");
    }

    public static int Main()
    {
        AutoNextWithHiddenWindow();
        StopsAndManualSelection();
        PostStopAndEnsemble();
        Console.WriteLine($"PASS {count} Windbound song-flow assertions.");
        return 0;
    }
}
