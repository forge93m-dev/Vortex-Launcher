using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;

namespace VortexLauncher.Views;

/// <summary>
/// Java, memory and JVM arguments for one instance. Each setting can follow the launcher defaults (the
/// checkbox is off) or be overridden here; changes apply the next time the game starts.
/// </summary>
public sealed class InstanceLaunchSettingsPanel : UserControl
{
    public readonly record struct Defaults(int RamGb, string JavaPath, string JvmArgs);

    private readonly InstanceModel _instance;
    private readonly Func<Defaults> _defaults;
    private readonly Action _save;
    private readonly CheckBox _ramToggle = new() { Name = "launchRamToggle" };
    private readonly Slider _ramSlider = new() { Name = "launchRamSlider", Minimum = 2, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 220 };
    private readonly TextBlock _ramValue = new() { Name = "launchRamValue", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ramHint = new() { Name = "launchRamHint", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _javaBox = new() { Name = "launchJavaBox", MaxLength = LaunchSettings.MaxTextLength };
    private readonly TextBlock _javaStatus = new() { Name = "launchJavaStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly CheckBox _argsToggle = new() { Name = "launchArgsToggle" };
    private readonly TextBox _argsBox = new() { Name = "launchArgsBox", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 64, MaxLength = LaunchSettings.MaxTextLength };
    private readonly Button _reset = new() { Name = "launchReset", Content = "Use launcher defaults", HorizontalAlignment = HorizontalAlignment.Left };
    private bool _loading;
    public int SavedCount { get; private set; }

    public InstanceLaunchSettingsPanel(InstanceModel instance, Func<Defaults> defaults, Action save)
    {
        _instance = instance; _defaults = defaults; _save = save;
        _ramToggle.Content = "Use a different amount of memory for this instance";
        _argsToggle.Content = "Use different JVM arguments for this instance";
        _javaBox.Watermark = "Launcher default (automatic detection)";
        _reset.Classes.Add("SecondaryBtn");
        foreach (var hint in new[] { _ramHint, _javaStatus }) hint.Classes.Add("Muted");

        var ramRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        ramRow.Children.Add(_ramSlider); ramRow.Children.Add(_ramValue);
        var title = new TextBlock { Text = "Launch settings for this instance", FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var intro = new TextBlock { Text = "Leave a setting off to follow the launcher's Settings. Changes apply the next time the game starts.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        intro.Classes.Add("Muted");
        var javaLabel = new TextBlock { Text = "Java", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        var argsHint = new TextBlock { Text = "Memory is set above, so leave -Xmx and -Xms out. Wrong arguments can stop the game from starting.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        argsHint.Classes.Add("Muted");
        var stack = new StackPanel { Spacing = 8 };
        foreach (Control child in new Control[] { title, intro, _ramToggle, ramRow, _ramHint, javaLabel, _javaBox, _javaStatus, _argsToggle, _argsBox, argsHint, _reset })
            stack.Children.Add(child);
        var border = new Border { Padding = new Thickness(18), Child = stack };
        border.Classes.Add("WorkspaceAside");
        Content = border; MaxWidth = 850; HorizontalAlignment = HorizontalAlignment.Left;

        _ramToggle.IsCheckedChanged += (_, _) => OnRamToggle();
        _ramSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnRamSlider(); };
        _ramSlider.PointerCaptureLost += (_, _) => Commit();
        _ramSlider.LostFocus += (_, _) => Commit();
        _javaBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowJavaStatus(); };
        _javaBox.LostFocus += (_, _) => CommitJava();
        _argsToggle.IsCheckedChanged += (_, _) => OnArgsToggle();
        _argsBox.LostFocus += (_, _) => CommitArgs();
        _reset.Click += (_, _) => { LaunchSettings.Reset(_instance); Refresh(); Commit(); };
        Refresh();
    }

    /// <summary>Redraws every control from the instance (also used after a reset).</summary>
    public void Refresh()
    {
        _loading = true;
        try
        {
            var defaults = _defaults();
            int systemGb = LaunchSettings.SystemRamGb();
            int ceiling = Math.Clamp(systemGb > 0 ? systemGb : 16, 4, LaunchSettings.MaxRamGb);
            _ramSlider.Maximum = Math.Max(ceiling, Math.Max(_instance.RamGbOverride, defaults.RamGb));
            bool ramOn = _instance.RamGbOverride > 0;
            _ramToggle.IsChecked = ramOn;
            _ramSlider.IsEnabled = ramOn;
            _ramSlider.Value = ramOn ? _instance.RamGbOverride : Math.Clamp(defaults.RamGb, _ramSlider.Minimum, _ramSlider.Maximum);
            ShowRam(defaults);
            _javaBox.Text = _instance.JavaPathOverride;
            ShowJavaStatus();
            bool argsOn = _instance.JvmArgsOverride != null;
            _argsToggle.IsChecked = argsOn;
            _argsBox.IsEnabled = argsOn;
            _argsBox.Text = argsOn ? _instance.JvmArgsOverride : defaults.JvmArgs;
            _reset.IsEnabled = LaunchSettings.HasOverrides(_instance);
        }
        finally { _loading = false; }
    }

    private void ShowRam(Defaults defaults)
    {
        int gb = (int)_ramSlider.Value;
        bool on = _ramToggle.IsChecked == true;
        _ramValue.Text = on ? $"{gb} GB" : $"{defaults.RamGb} GB (launcher)";
        string warning = on ? LaunchSettings.RamWarning(gb, LaunchSettings.SystemRamGb()) : "";
        _ramHint.Text = warning.Length > 0 ? warning : on ? "Used only for this instance." : "Follows the memory set in the launcher's Settings.";
    }

    private void ShowJavaStatus()
    {
        string? problem = LaunchSettings.CheckJavaPath(_javaBox.Text);
        _javaStatus.Text = string.IsNullOrWhiteSpace(_javaBox.Text) ? "Automatic: the launcher picks the Java this game version needs." : problem ?? "This Java will be used for this instance.";
    }

    private void OnRamToggle()
    {
        if (_loading) return;
        var defaults = _defaults();
        if (_ramToggle.IsChecked == true)
        {
            _instance.RamGbOverride = LaunchSettings.ClampRam((int)Math.Clamp(defaults.RamGb, _ramSlider.Minimum, _ramSlider.Maximum));
            _ramSlider.IsEnabled = true; _loading = true; _ramSlider.Value = _instance.RamGbOverride; _loading = false;
        }
        else { _instance.RamGbOverride = 0; _ramSlider.IsEnabled = false; }
        ShowRam(defaults); _reset.IsEnabled = LaunchSettings.HasOverrides(_instance); Commit();
    }

    private void OnRamSlider()
    {
        if (_loading || _ramToggle.IsChecked != true) return;
        _instance.RamGbOverride = LaunchSettings.ClampRam((int)_ramSlider.Value);
        ShowRam(_defaults());
    }

    private void CommitJava()
    {
        if (_loading) return;
        string text = (_javaBox.Text ?? "").Trim();
        if (text == _instance.JavaPathOverride) return;
        _instance.JavaPathOverride = text; _reset.IsEnabled = LaunchSettings.HasOverrides(_instance); Commit();
    }

    private void OnArgsToggle()
    {
        if (_loading) return;
        bool on = _argsToggle.IsChecked == true;
        _argsBox.IsEnabled = on;
        // Starting from the launcher's arguments, so turning this on never silently drops them.
        _instance.JvmArgsOverride = on ? (_argsBox.Text ?? _defaults().JvmArgs) : null;
        if (!on) { _loading = true; _argsBox.Text = _defaults().JvmArgs; _loading = false; }
        _reset.IsEnabled = LaunchSettings.HasOverrides(_instance); Commit();
    }

    private void CommitArgs()
    {
        if (_loading || _argsToggle.IsChecked != true) return;
        string text = _argsBox.Text ?? "";
        if (text == _instance.JvmArgsOverride) return;
        _instance.JvmArgsOverride = text; Commit();
    }

    /// <summary>Applies pending text edits and saves the instance.</summary>
    public void Commit()
    {
        if (_loading) return;
        string text = (_javaBox.Text ?? "").Trim();
        if (text != _instance.JavaPathOverride) _instance.JavaPathOverride = text;
        if (_argsToggle.IsChecked == true && _argsBox.Text != _instance.JvmArgsOverride) _instance.JvmArgsOverride = _argsBox.Text ?? "";
        LaunchSettings.Sanitize(_instance);
        _save(); SavedCount++;
    }
}
