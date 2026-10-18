using VortexLauncher.Core.Launch;
using VortexLauncher.Views;

internal static class SessionConsoleUiChecks
{
    public static void Run(Action<int> pump)
    {
        var sessions = new SessionWindows<DebugLogWindow>(window => window.Close());
        DebugLogWindow Open()
        {
            var window = new DebugLogWindow();
            sessions.Add(window);
            window.Closed += (_, _) => sessions.Forget(window);
            window.Show(); pump(30); return window;
        }
        var first = Open();
        first.AppendLog("Fixture game started"); pump(30);
        Check(first.Lines.Count == 1, "real console receives game output");
        first.AppendLog("queued before close");
        sessions.Finish(first, false); pump(30);
        Check(!first.IsVisible && first.Lines.Count == 1, "normal exit closes real console and drops queued visual updates");
        first.AppendLog("output after manual close"); pump(30);
        Check(first.Lines.Count == 1 && first.GetLogSnapshot().Length == 3, "closed console keeps bounded diagnostic capture without UI work");
        var crash = Open(); sessions.Finish(crash, true);
        Check(crash.IsVisible, "failed session remains readable");
        var next = Open();
        Check(!crash.IsVisible && next.IsVisible, "next game replaces completed error console");
        var manuallyClosed = Open(); manuallyClosed.Close();
        sessions.Finish(manuallyClosed, true);
        sessions.Shutdown(); pump(30);
        Check(!next.IsVisible && !manuallyClosed.IsVisible, "shutdown closes all surviving real windows; manual close is safe");
        Console.WriteLine("SESSION_CONSOLE_UI_OK (headless, no game or real profiles)");
    }
    private static void Check(bool value,string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
