using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Management;

public partial class DownloadOperation : ObservableObject, IDisposable
{
    public string Title { get; }
    public string Started { get; } = DateTime.Now.ToString("HH:mm:ss");
    [ObservableProperty] private string _stage = "Preparing";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _state = "Active";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _indeterminate = true;
    public bool CanCancel => State == "Active" && _cancel != null;
    private readonly Action? _cancel;
    private int _finished;
    internal DownloadOperation(string title, Action? cancel) { Title = title; _cancel = cancel; }
    partial void OnStateChanged(string value) => OnPropertyChanged(nameof(CanCancel));
    internal static void Ui(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
    [RelayCommand] private void Cancel()
    {
        if (!CanCancel) return;
        _cancel?.Invoke();
        State = "Stopping";
        Details = "Stopping safely; verified files remain cached.";
    }
    public void Report(string stage, double? percent = null, string? details = null)
    {
        if (Volatile.Read(ref _finished) != 0) return;
        Ui(() => { if (_finished != 0 && State != "Active" && State != "Stopping") return;
            Stage = stage; if (details != null) Details = details;
            if (percent.HasValue) { Percent = Math.Clamp(percent.Value, 0, 100); Indeterminate = false; } });
    }
    public void Report(TransferSnapshot snapshot) => Report(snapshot.Stage, snapshot.Percent, snapshot.Details);
    public void Fail(Exception exception) => Finish("Failed", exception.Message);
    public void Cancelled() => Finish("Cancelled", "Verified files are saved. Retry from the instance or content browser.");
    public void Complete(string details = "Completed successfully") => Finish("Completed", details);
    private void Finish(string state, string details)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        Ui(() => { State = state; Details = details; Indeterminate = false; if (state == "Completed") Percent = 100; });
    }
    public void Dispose() { if (_finished == 0) Complete(); }
}

public static class DownloadCenter
{
    public static ObservableCollection<DownloadOperation> Operations { get; } = new();
    private static readonly AsyncLocal<DownloadOperation?> CurrentOperation = new();
    public static DownloadOperation? Current => CurrentOperation.Value;
    public static DownloadOperation Begin(string title, Action? cancel = null)
    {
        var operation = new DownloadOperation(title, cancel);
        CurrentOperation.Value = operation;
        DownloadOperation.Ui(() =>
        {
            Operations.Insert(0, operation);
            while (Operations.Count > 100)
            {
                var oldest = Operations.LastOrDefault(o => o.State is "Completed" or "Failed" or "Cancelled");
                if (oldest == null) break;
                Operations.Remove(oldest);
            }
        });
        return operation;
    }
}
