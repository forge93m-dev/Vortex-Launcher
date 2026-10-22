using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VortexLauncher.Core.Launch;

internal sealed class InstallationTiming : IDisposable
{
    private readonly string _instance;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private readonly List<string> _lines = new();
    private TimeSpan _previous;
    private bool _complete;
    internal InstallationTiming(string instance, string operation)
    {
        _instance = instance;
        _lines.Add($"Operation: {operation}; started: {DateTimeOffset.UtcNow:O}");
    }
    internal void Mark(string stage)
    {
        TimeSpan elapsed = _timer.Elapsed;
        _lines.Add(FormattableString.Invariant($"{stage}: {(elapsed - _previous).TotalSeconds:0.000}s (total {elapsed.TotalSeconds:0.000}s)"));
        _previous = elapsed;
    }
    internal void Complete() { Mark("Finalization"); _complete = true; }
    public void Dispose()
    {
        _lines.Add(FormattableString.Invariant($"Total installation: {_timer.Elapsed.TotalSeconds:0.000}s; completed: {_complete}"));
        try { File.WriteAllLines(Path.Combine(_instance, ".vortex-installation-stages.txt"), _lines); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
