using VortexLauncher.Core.Launch;

internal static class SessionWindowChecks
{
    public static void Run()
    {
        var closed = new List<object>();
        var sessions = new SessionWindows<object>(closed.Add);
        var first = new object();
        sessions.Add(first);
        sessions.Finish(first, false);
        Check(closed.SequenceEqual(new[] { first }), "normal game exit closes its console");
        var crash = new object();
        sessions.Add(crash);
        sessions.Finish(crash, true);
        Check(closed.Count == 1, "crash console remains available for diagnostics");
        var running = new object();
        sessions.Add(running);
        Check(closed.SequenceEqual(new[] { first, crash }), "next launch removes completed crash console");
        var concurrent = new object();
        sessions.Add(concurrent);
        Check(!closed.Contains(running), "new launch does not close a still-running game's console");
        sessions.Forget(concurrent); // User manually closes it; process exit is safe afterwards.
        sessions.Finish(concurrent, true);
        sessions.Shutdown();
        sessions.Shutdown();
        sessions.Finish(running, true);
        Check(closed.Count == 3 && closed[^1] == running, "launcher shutdown closes remaining consoles exactly once; late exits do not reopen them");
        try { sessions.Add(new object()); throw new Exception("Shutdown accepted a new console"); }
        catch (InvalidOperationException) { }
        var reentrant = new SessionWindows<object>(w => { });
        reentrant = new SessionWindows<object>(w => reentrant.Forget(w));
        reentrant.Add(new object()); reentrant.Shutdown();
        Console.WriteLine("SESSION_WINDOWS_OK");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
