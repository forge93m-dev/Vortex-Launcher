using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using VortexLauncher.Core.Diagnostics;

namespace VortexLauncher.Views;

public sealed class TaskDialog : Window
{
    public TaskDialog(string title, string message, string? confirm = null, string? details = null, string? folder = null)
    {
        Title = title; Width = 660; Height = details == null ? 420 : 560; MinWidth = 500; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = confirm == null ? "Close" : "Cancel" };
        cancel.Classes.Add("SecondaryBtn"); cancel.Click += (_, _) => Close(false);
        actions.Children.Add(cancel);
        if (folder != null)
        {
            var open = new Button { Content = "Open instance folder" }; open.Classes.Add("SecondaryBtn");
            open.Click += (_, _) => { try { if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); } catch (Exception ex) { open.Content = ex.Message; } };
            actions.Children.Insert(0, open);
        }
        if (details != null)
        {
            var copy = new Button { Content = "Copy details" }; copy.Classes.Add("SecondaryBtn");
            copy.Click += async (_, _) => { if (Clipboard != null) { await Clipboard.SetTextAsync(title + "\n" + message + "\n\n" + details); copy.Content = "Copied"; } };
            actions.Children.Insert(0, copy);
            var report = new Button { Content = "Save error report" }; report.Classes.Add("SecondaryBtn");
            report.Click += async (_, _) =>
            {
                report.IsEnabled = false;
                try { report.Content = "Saving…"; string result = await DiagnosticReportExport.SaveAsync(this, folder, new[] { title, message, details }); report.Content = result.StartsWith("Report saved:", StringComparison.Ordinal) ? "Report saved" : "Save error report"; ToolTip.SetTip(report, result); }
                catch (Exception ex) { report.Content = "Report failed"; ToolTip.SetTip(report, ex.Message); }
                finally { report.IsEnabled = true; }
            };
            actions.Children.Insert(0, report);
        }
        if (confirm != null)
        {
            var proceed = new Button { Content = confirm }; proceed.Classes.Add("PrimaryBtn");
            proceed.Click += (_, _) => Close(true); actions.Children.Add(proceed);
        }
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var content = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Spacing = 8 };
        var category = new TextBlock { Text = details != null ? "WORKSPACE / DIAGNOSTICS" : "WORKSPACE / REVIEW" };
        category.Classes.Add("LibraryMetaLabel"); heading.Children.Add(category);
        heading.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var header = new Border { Child = heading }; header.Classes.Add("DialogHeading"); content.Children.Add(header);
        var description = new TextBlock { Text = message, FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap };
        description.Classes.Add("Muted"); content.Children.Add(description);
        if (details != null) content.Children.Add(new Expander { Header = "Technical details", Content = new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 240 } });
        panel.Children.Add(new ScrollViewer { Content = content }); Content = panel;
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(false); };
        WindowTitleBar.Apply(this);
    }
    public static Task<bool> ConfirmAsync(string title, string message, string confirm) =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }
            ? new TaskDialog(title, message, confirm).ShowDialog<bool>(ActiveOwner(owner)) : Task.FromResult(false);

    private static Window ActiveOwner(Window fallback) =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.LastOrDefault(w => w.IsActive) ?? fallback;

    public static void ShowIssue(LaunchIssue issue, string details, string? folder = null)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            _ = new TaskDialog(issue.Title, issue.Explanation + "\n\nWhat to do: " + issue.Action, details: details, folder: folder).ShowDialog<bool>(ActiveOwner(owner));
    }
}
