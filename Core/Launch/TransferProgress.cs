using System;
using System.Diagnostics;

namespace VortexLauncher.Core.Launch;

public sealed record TransferSnapshot(string Stage, double Percent, string Details);

public sealed class TransferProgress
{
    private readonly object _gate = new();
    private readonly Action<TransferSnapshot> _report;
    private readonly Stopwatch _clock = new();
    private string _stage = string.Empty;
    private long _totalBytes, _readyBytes, _networkBytes;
    private int _totalFiles, _readyFiles;
    private double _lastReport;

    public TransferProgress(Action<TransferSnapshot> report) => _report = report;

    public void Begin(string stage, int files, long bytes)
    {
        lock (_gate)
        {
            _stage = stage;
            _totalFiles = files;
            _totalBytes = bytes;
            _readyBytes = _networkBytes = 0;
            _readyFiles = 0;
            _lastReport = -1;
            _clock.Restart();
            Publish(true);
        }
    }

    public void Received(long bytes)
    {
        lock (_gate)
        {
            _readyBytes += bytes;
            if (bytes > 0) _networkBytes += bytes;
            Publish(false);
        }
    }

    public void Complete(long fileSize, long received)
    {
        lock (_gate)
        {
            _readyBytes += Math.Max(0, fileSize - received);
            _readyFiles++;
            Publish(_readyFiles == _totalFiles);
        }
    }

    private void Publish(bool force)
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        if (!force && seconds - _lastReport < 0.2) return;
        _lastReport = seconds;
        double speed = seconds > 0 ? _networkBytes / seconds : 0;
        double percent = _totalFiles == 0 ? 100 : _totalBytes > 0
            ? Math.Clamp((double)_readyBytes / _totalBytes * 100, 0, 100)
            : (double)_readyFiles / _totalFiles * 100;
        string bytesText = _totalBytes > 0
            ? $"{FormatBytes(Math.Min(_readyBytes, _totalBytes))} / {FormatBytes(_totalBytes)}"
            : FormatBytes(_readyBytes);
        string speedText = _networkBytes == 0 ? "Using saved files" : $"{FormatBytes((long)speed)}/s";
        string remaining = speed > 0 && _totalBytes > _readyBytes
            ? $" • ~{TimeSpan.FromSeconds(Math.Min(86400, (_totalBytes - _readyBytes) / speed)):mm\\:ss} remaining"
            : string.Empty;
        _report(new TransferSnapshot(_stage, percent,
            $"{_readyFiles}/{_totalFiles} files • {bytesText} • {speedText}{remaining}"));
    }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";
}
