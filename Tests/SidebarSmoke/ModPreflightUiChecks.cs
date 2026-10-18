using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class ModPreflightUiChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        string root = Directory.CreateDirectory(Path.Combine(output, "mod-report")).FullName;
        var error = new ModCheckIssue(true, "missing-dependency", "example-long-filename-fabric-26.3.jar", "Missing required mod 'fabric-api'", "Install the compatible Fabric API version or disable the mod that needs it.");
        var warning = new ModCheckIssue(false, "unverified", "unknown.jar", "Could not verify this file", "The file is unrecognized. No incompatibility was guessed.");
        foreach (var report in new[] { new ModPreflightReport(new[] { error, warning }, 2, 1, 1), new ModPreflightReport(new[] { warning }, 1, 0, 1), new ModPreflightReport(Array.Empty<ModCheckIssue>(), 0, 0, 0) })
        {
            var dialog = new ModCheckWindow(report, root, beforeLaunch: true) { Width = 580, Height = 440 };
            dialog.Show(); pump(100);
            var actions = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string).ToArray();
            Check(actions.Any(b => b.Content as string == "Launch with warnings") == !report.HasErrors, "only non-blocking reports expose a launch action");
            Check(dialog.AllowsLaunch == !report.HasErrors, "dialog launch gate reflects report severity");
            foreach (var action in actions)
            {
                var point = action.TranslatePoint(default, dialog)!.Value;
                Check(point.X >= 0 && point.Y >= 0 && point.X + action.Bounds.Width <= dialog.ClientSize.Width + .5 && point.Y + action.Bounds.Height <= dialog.ClientSize.Height + .5, "mod report actions fit the smallest supported window");
            }
            if (report.HasErrors)
            {
                Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == error.File), "report names the problematic file");
                dialog.CaptureRenderedFrame()!.Save(Path.Combine(output, "mod-check-errors-minimum.png"));
            }
            dialog.Close();
        }
        var longReport = new ModPreflightReport(Enumerable.Range(0, 80)
            .Select(i => warning with { File = $"mod-{i}.jar" }).ToArray(), 80, 0, 0);
        var recycling = new ModCheckWindow(longReport, root, beforeLaunch: true);
        recycling.Show(); pump(80);
        var list = recycling.GetVisualDescendants().OfType<ListBox>().Single();
        Check(list.ItemTemplate!.Build(null) is Control, "cleared virtualized items are safe to render");
        foreach (int index in new[] { 79, 0, 50, 10, 79 })
        {
            list.ScrollIntoView(list.Items[index]!); pump(40);
            recycling.Height = recycling.Height == 540 ? 440 : 540; pump(40);
        }
        list.ItemsSource = Array.Empty<ModCheckIssue>(); pump(60);
        recycling.Close(); pump(40);
        Check(true, "long warning lists survive scrolling, resizing, clearing and container recycling");
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        string jar = Path.Combine(root, "mods/consumer.jar");
        using (var zip = ZipFile.Open(jar, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"consumer\",\"version\":\"1\",\"depends\":{\"missing-api\":\"*\"}}");
        byte[] original = File.ReadAllBytes(jar);
        var target = new InstanceModel { Name = "Preflight fixture", InstancePath = root, Version = "26.3", Loader = "Fabric", LoaderVersion = "0.19.5", IsInstalled = true };
        var vm = new MainWindowViewModel(new InstanceManager(Path.Combine(root, "unused-instances")), new SettingsManager(Path.Combine(root, "unused-config")), new AccountManager(Path.Combine(root, "unused-config")), checkUpdates: false) { IsInitializing = false };
        int reviews = 0;
        vm.ReviewModCheckAsync = (report, path) => { Check(report.HasErrors && path == root, "real launch pipeline presents the blocking local report"); reviews++; return Task.FromResult(true); };
        var launch = vm.LaunchInstanceCommand.ExecuteAsync(target);
        Check(vm.IsDownloading && !target.CanChangeFiles, "launch preparation locks conflicting mod writes during inspection");
        Wait(launch, pump);
        Check(reviews == 1 && !vm.IsDownloading && !target.IsInstalling && !target.IsRunning && vm.StatusMessage.Contains("blocked"), "known errors stop actual launch preparation even if a reviewer returns approval");
        Check(File.ReadAllBytes(jar).SequenceEqual(original) && !Directory.Exists(Path.Combine(root, "logs")), "blocked preflight never changes mods or creates a game log window");
        vm.CancelStartup(); vm.Library.Dispose();

        using var mods = new InstanceModsViewModel("26.3", "Fabric", root, loaderVersion: "0.19.5");
        var release = new TaskCompletionSource();
        bool shown = false;
        mods.ShowModCheckAsync = report => { shown = report.HasErrors; return release.Task; };
        var scan = mods.CheckModsCommand.ExecuteAsync(null);
        var clock = Stopwatch.StartNew(); while (!shown && clock.ElapsedMilliseconds < 5000) pump(10);
        Check(shown && mods.IsBusy && !mods.CanMutate, "manual mod-manager check exposes the report and guards writes");
        release.SetResult(); Wait(scan, pump);
        Check(!mods.IsBusy && mods.StatusMessage.Contains("1 error"), "manual check releases its busy state and reports the error count");
        var embedded = new InstalledModsView { DataContext = mods };
        var host = new Window { Content = embedded, Width = 760, Height = 500 }; host.Show(); pump(80);
        var more = embedded.FindControl<Button>("ModsMoreActions")!;
        more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); pump(50);
        Check(more.ContextFlyout is MenuFlyout menu && menu.Items.OfType<MenuItem>().Any(b => b.Header as string == "Check mods" && ReferenceEquals(b.Command, mods.CheckModsCommand)), "embedded Mods menu exposes the working check command");
        more.ContextFlyout?.Hide();
        host.Close();
        Console.WriteLine("MOD_PREFLIGHT_UI_OK");
    }
    private static void Wait(Task task, Action<int> pump)
    {
        var clock = Stopwatch.StartNew(); while (!task.IsCompleted && clock.ElapsedMilliseconds < 10000) pump(10);
        if (!task.IsCompleted) throw new Exception("Mod check test timed out");
        task.GetAwaiter().GetResult(); pump(50);
    }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
}
