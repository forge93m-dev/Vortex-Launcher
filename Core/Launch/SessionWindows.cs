using System;
using System.Collections.Generic;
using System.Linq;

namespace VortexLauncher.Core.Launch;

/// <summary>UI-thread-only ownership of running and completed diagnostic windows.</summary>
public sealed class SessionWindows<T> where T : class
{
    private readonly Dictionary<T, bool> _windows = new(ReferenceEqualityComparer.Instance);
    private readonly Action<T> _close;
    public bool IsShutdown { get; private set; }
    public SessionWindows(Action<T> close) => _close = close;
    public void Add(T window)
    {
        if (IsShutdown) throw new InvalidOperationException("Launcher is closed.");
        foreach (var old in _windows.Where(pair => pair.Value).Select(pair => pair.Key).ToArray()) Close(old);
        _windows.Add(window, false);
    }
    public void Forget(T window) => _windows.Remove(window);
    public void Finish(T window, bool keepForError)
    {
        if (!_windows.ContainsKey(window)) return;
        if (!IsShutdown && keepForError) _windows[window] = true;
        else Close(window);
    }
    private void Close(T window)
    {
        if (_windows.Remove(window)) _close(window);
    }
    public void Shutdown()
    {
        if (IsShutdown) return;
        IsShutdown = true;
        foreach (var window in _windows.Keys.ToArray()) Close(window);
    }
}
