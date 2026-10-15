using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace VortexLauncher.Views;

public partial class DebugLogWindow : Window
{
    public ObservableCollection<LogLine> Lines { get; } = new();
    private readonly List<string> _rawLines = new();
    private readonly object _rawLinesLock = new();
    public string? InstancePath { get; set; }
    private async void OnExportReportClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try { ReportStatus.Text = await DiagnosticReportExport.SaveAsync(this, InstancePath, GetLogSnapshot()); }
        catch (Exception ex) { ReportStatus.Text = "Could not save report: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }

    public DebugLogWindow()
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        LogItems.ItemsSource = Lines;
    }

    public void AppendLog(string text)
    {
        lock (_rawLinesLock)
        {
            _rawLines.Add(text);
            if (_rawLines.Count > 5000) _rawLines.RemoveRange(0, 500);
        }

        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(new LogLine(text, GetColor(text)));
            if (Lines.Count > 5000) for (int i = 0; i < 500; i++) Lines.RemoveAt(0);
            LogScrollViewer.ScrollToEnd();
        });
    }

    public string[] GetLogSnapshot()
    {
        lock (_rawLinesLock) return _rawLines.ToArray();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Lines.Clear();
        lock (_rawLinesLock) _rawLines.Clear();
    }

    private static IBrush GetColor(string text)
    {
        if (text.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Crash", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.OrangeRed;
        }

        if (text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Red;
        }

        if (text.Contains("WARN", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Gold;
        }

        if (Avalonia.Application.Current?.Resources.TryGetValue("ThemeSubtextBrush", out var brush) == true &&
            brush is IBrush themeBrush)
        {
            return themeBrush;
        }

        return new SolidColorBrush(Color.Parse("#A9B1D6"));
    }
}

public sealed record LogLine(string Text, IBrush Color);
