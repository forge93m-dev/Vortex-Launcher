using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;
using VortexLauncher.Views;

// The per-instance launch settings panel: follows the launcher until a setting is switched on, saves on commit.
internal static class LaunchSettingsPanelChecks
{
    internal static void Run(Action<int> pump)
    {
        var instance = new InstanceModel { Name = "Panel fixture", Version = "1.20.1", Loader = "Vanilla" };
        int saves = 0;
        var defaults = new InstanceLaunchSettingsPanel.Defaults(6, "", "-XX:+UseG1GC");
        var panel = new InstanceLaunchSettingsPanel(instance, () => defaults, () => saves++);
        T Part<T>(string name) where T : Control => panel.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
        var ramToggle = Part<CheckBox>("launchRamToggle"); var slider = Part<Slider>("launchRamSlider"); var ramValue = Part<TextBlock>("launchRamValue");
        var javaBox = Part<TextBox>("launchJavaBox"); var javaStatus = Part<TextBlock>("launchJavaStatus");
        var argsToggle = Part<CheckBox>("launchArgsToggle"); var argsBox = Part<TextBox>("launchArgsBox"); var reset = Part<Button>("launchReset");

        Check(ramToggle.IsChecked == false && !slider.IsEnabled && ramValue.Text == "6 GB (launcher)" && !argsBox.IsEnabled && argsBox.Text == "-XX:+UseG1GC" && !reset.IsEnabled && saves == 0,
            "a fresh instance follows the launcher: nothing is overridden, the launcher's values are shown, nothing is saved");

        ramToggle.IsChecked = true;
        Check(instance.RamGbOverride == 6 && slider.IsEnabled && reset.IsEnabled && saves == 1 && ramValue.Text == "6 GB", "switching memory on starts from the launcher's amount and saves");
        slider.Value = 9; pump(40);
        Check(instance.RamGbOverride == 9 && ramValue.Text == "9 GB" && saves == 1, "dragging the memory slider updates the instance at once but saves only when released");
        panel.Commit();
        Check(saves == 2, "releasing the slider saves the instance");
        ramToggle.IsChecked = false;
        Check(instance.RamGbOverride == 0 && !slider.IsEnabled && ramValue.Text == "6 GB (launcher)" && saves == 3, "switching memory off returns to the launcher's amount");

        javaBox.Text = Path.Combine(Path.GetTempPath(), "no-such-folder", "java.exe");
        Check(javaStatus.Text!.Contains("does not exist"), "a Java path that does not exist is explained before launching");
        javaBox.Text = Path.Combine(Path.GetTempPath(), "notepad.exe");
        Check(javaStatus.Text!.Contains("Choose the java"), "a file that is not Java is explained");
        string javaDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vortex-panel-java-" + Guid.NewGuid().ToString("N"), "bin")).FullName;
        string java = Path.Combine(javaDir, "java.exe"); File.WriteAllText(java, "x");
        javaBox.Text = java; panel.Commit();
        Check(instance.JavaPathOverride == java && javaStatus.Text.Contains("will be used") && saves >= 4, "a real Java file is used for this instance and saved");
        javaBox.Text = ""; panel.Commit();
        Check(instance.JavaPathOverride == "" && javaStatus.Text!.Contains("Automatic"), "clearing the path returns to automatic Java");

        argsToggle.IsChecked = true;
        Check(instance.JvmArgsOverride == "-XX:+UseG1GC" && argsBox.IsEnabled, "turning arguments on starts from the launcher's arguments, so nothing is silently dropped");
        argsBox.Text = "-XX:+UseZGC"; panel.Commit();
        Check(instance.JvmArgsOverride == "-XX:+UseZGC", "edited arguments are stored for this instance");
        argsBox.Text = ""; panel.Commit();
        Check(instance.JvmArgsOverride == "", "an empty argument box is a real choice: no extra arguments");
        argsToggle.IsChecked = false;
        Check(instance.JvmArgsOverride == null && !argsBox.IsEnabled && argsBox.Text == "-XX:+UseG1GC", "turning arguments off follows the launcher again");

        ramToggle.IsChecked = true; argsToggle.IsChecked = true; javaBox.Text = java; panel.Commit();
        int before = saves;
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!LaunchSettings.HasOverrides(instance) && ramToggle.IsChecked == false && argsToggle.IsChecked == false && javaBox.Text == "" && !reset.IsEnabled && saves == before + 1,
            "\"Use launcher defaults\" clears everything, redraws the panel and saves once");

        defaults = new InstanceLaunchSettingsPanel.Defaults(12, "", "");
        panel.Refresh();
        Check(slider.Maximum >= 12 && ramValue.Text == "12 GB (launcher)", "a launcher default above the usual range still fits the slider");
        instance.RamGbOverride = 40; panel.Refresh();
        Check(slider.Maximum >= 40 && slider.Value == 40 && ramToggle.IsChecked == true, "a larger saved value is shown, not cut down");

        // Inside the instance workspace the panel sits on the Settings tab.
        var workspace = new InstanceWorkspaceView(new InstanceModel { Name = "Workspace fixture", Version = "1.20.1", Loader = "Vanilla" });
        int workspaceSaves = 0;
        workspace.AttachLaunchSettings(() => new(4, "", ""), () => workspaceSaves++);
        workspace.SelectSection("Settings"); pump(40);
        Check(workspace.LaunchSettings != null && workspace.FindControl<ScrollViewer>("SettingsPage")!.GetLogicalDescendants().OfType<InstanceLaunchSettingsPanel>().Count() == 1,
            "the instance's Settings tab shows its launch settings");
        // A rendered preview next to the other captures, for a quick visual review.
        var host = new Window { Width = 1000, Height = 780, Content = workspace };
        host.Show(); workspace.AttachLaunchSettings(() => new(6, "", "-XX:+UseG1GC"), () => { }); workspace.SelectSection("Settings"); pump(120);
        workspace.LaunchSettings!.Refresh(); pump(40);
        host.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "vortex-launch-settings-preview.png"));
        host.Close();
        Console.WriteLine("LAUNCH_SETTINGS_PANEL_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
