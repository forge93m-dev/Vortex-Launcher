using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Views;

public sealed class ModCheckWindow : Window
{
    public ModPreflightReport Report { get; }
    public bool AllowsLaunch { get; }
    public ModCheckWindow(ModPreflightReport report, string root, bool beforeLaunch = false)
    {
        Report = report; AllowsLaunch = beforeLaunch && !report.HasErrors;
        Title = "Mod check"; Width = 760; Height = 540; MinWidth = 580; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(22), LastChildFill = true };
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = report.Headline, FontSize = 23, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = report.Counts, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = report.Notes, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var open = Action("Open mods folder", () => { try { string mods = Path.Combine(root, "mods"); if (Directory.Exists(mods)) PlatformSupport.OpenPath(mods); } catch { } });
        var copy = Action("Copy report", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(report.Summary); });
        var close = Action(beforeLaunch ? "Back" : "Close", () => Close(false));
        actions.Children.Add(open); actions.Children.Add(copy); actions.Children.Add(close);
        if (AllowsLaunch)
        {
            var proceed = Action("Launch with warnings", () => Close(true)); proceed.Classes.Remove("SecondaryBtn"); proceed.Classes.Add("PrimaryBtn");
            actions.Children.Add(proceed);
        }
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var list = new ListBox { ItemsSource = report.Issues };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } });
        list.ItemTemplate = new FuncDataTemplate<ModCheckIssue>((issue, _) =>
        {
            // Avalonia clears the item while recycling a virtualized container.
            if (issue is null) return new Border();
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock { Text = issue.Heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse(issue.IsError ? "#FF9C9C" : issue.IsInformational ? "#A7B7D0" : "#E9D993")) });
            body.Children.Add(new TextBlock { Text = issue.File, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = issue.Advice, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var row = new Border { Child = body, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(7) };
            row.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemePanelBrush"));
            return row;
        });
        panel.Children.Add(list);
        Content = panel;
        WindowTitleBar.Apply(this);
        Opened += (_, _) => close.Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } };
    }
    private static Button Action(string title, Action click)
    {
        var button = new Button { Content = title, Margin = new Thickness(6, 3, 0, 3) };
        button.Classes.Add("SecondaryBtn"); button.Click += (_, _) => click(); return button;
    }
}
