using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class ModManagementUiChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        using var f = new ModFixture(output);
        f.Add("alpha"); f.Add("bravo"); f.Add("disabled", disabled: true);
        using var vm = new InstanceModsViewModel("26.3", "Fabric", f.Root, f.Service);
        var window = new InstanceModsWindow { DataContext = vm, Width = 760, Height = 600 };
        window.Show(); pump(100);
        Check(vm.InstalledMods.Count == 3 && window.FindControl<ListBox>("InstalledModsList")!.ItemCount == 3, "mod manager shows a virtualized list bound to real installed files");
        string? page = null; vm.OpenExternalUrl = url => page = url;
        Wait(vm.OpenModPageCommand.ExecuteAsync(vm.InstalledMods.Single(m => m.Name == "alpha-old")), pump);
        Check(page == "https://modrinth.com/mod/alpha", "mod page uses exact hash identity without opening a real browser in tests");
        vm.SearchQuery = "alpha"; pump(20);
        vm.SelectVisibleCommand.Execute(null);
        Check(vm.SelectedCount == 1 && vm.VisibleMods.Single().Name == "alpha-old", "search and select shown affect only visible mods");
        vm.SearchQuery = ""; vm.SelectedFilter = "Enabled";
        Check(vm.VisibleMods.Count == 2, "enabled filter excludes disabled mods");
        vm.DisableSelectedCommand.Execute(null);
        Check(File.Exists(Path.Combine(f.Root, "mods", "alpha-old.jar.disabled")) && vm.VisibleMods.Count == 1, "bulk disable preserves .jar filename and refreshes state filter");
        vm.SelectedFilter = "Disabled"; vm.SelectVisibleCommand.Execute(null); vm.EnableSelectedCommand.Execute(null);
        Check(File.Exists(Path.Combine(f.Root, "mods", "alpha-old.jar")) && File.Exists(Path.Combine(f.Root, "mods", "disabled-old.jar")), "bulk enable restores selected disabled mods");
        vm.SelectedFilter = "All";
        var alpha = vm.InstalledMods.Single(m => m.Name == "alpha-old");
        alpha.IsEnabled = false; vm.ToggleModCommand.Execute(alpha);
        Check(File.Exists(Path.Combine(f.Root, "mods", "alpha-old.jar.disabled")), "toggle uses actual disk state, not an already-updated toggle binding");
        vm.CanChangeFiles = () => false; vm.EnableSelectedCommand.Execute(null);
        Check(File.Exists(Path.Combine(f.Root, "mods", "alpha-old.jar.disabled")), "bulk management respects busy/running instance guard");
        vm.CanChangeFiles = () => true;
        vm.ClearSelectionCommand.Execute(null); vm.SearchQuery = "bravo"; vm.SelectVisibleCommand.Execute(null);
        vm.ConfirmRemovalAsync = _ => Task.FromResult(false);
        Wait(vm.RemoveSelectedCommand.ExecuteAsync(null), pump);
        Check(File.Exists(Path.Combine(f.Root, "mods", "bravo-old.jar")), "bulk removal cancellation leaves all selected files unchanged");
        vm.ConfirmRemovalAsync = _ => Task.FromResult(true);
        Wait(vm.RemoveSelectedCommand.ExecuteAsync(null), pump);
        Check(!File.Exists(Path.Combine(f.Root, "mods", "bravo-old.jar")) && Directory.GetFiles(Path.Combine(f.Root, ".vortex-trash"), "*", SearchOption.AllDirectories).Length == 1, "bulk removal preserves original content in recoverable backup");
        vm.SearchQuery = ""; pump(30);
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "mod-management.png"));
        Wait(vm.CheckUpdatesCommand.ExecuteAsync(null), pump);
        Check(vm.IsPreviewVisible && vm.AvailableUpdates.Count == 2 && f.Handler.Requests == 0, "update check opens a read-only preview without downloading or replacing mods");
        vm.AvailableUpdates[0].IsSelected = false;
        Check(vm.SelectedUpdateCount == 1 && vm.CanApplyUpdates, "individual update selection changes the apply count");
        pump(80);
        foreach (var action in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is string s && (s == "Check updates" || s == "Update selected…" || s == "Close")))
        {
            var p = action.TranslatePoint(default, window)!.Value;
            Check(p.X >= 0 && p.X + action.Bounds.Width <= window.ClientSize.Width && p.Y + action.Bounds.Height <= window.ClientSize.Height, "update preview action fits the smallest supported window");
        }
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "mod-update-preview.png"));
        vm.ConfirmUpdatesAsync = _ => Task.FromResult(false);
        Wait(vm.ApplyUpdatesCommand.ExecuteAsync(null), pump);
        Check(f.Handler.Requests == 0, "update confirmation cancellation does not download anything");
        vm.ConfirmUpdatesAsync = _ => Task.FromResult(true);
        Wait(vm.ApplyUpdatesCommand.ExecuteAsync(null), pump);
        Check(!vm.IsBusy && vm.AvailableUpdates.Count == 0 && f.Handler.Requests == 1, "applying one selected update refreshes mods and clears the stale preview");
        window.Close();
        var console = new DebugLogWindow { InstancePath = f.Root }; console.Show(); pump(20);
        Check(console.FindControl<Button>("ExportReportButton")!.IsEffectivelyVisible, "game console exposes the local error-report export action");
        console.Close();
        Console.WriteLine("MOD_MANAGEMENT_UI_OK");
    }
    private static void Wait(Task task, Action<int> pump)
    {
        var timer = Stopwatch.StartNew(); while (!task.IsCompleted && timer.ElapsedMilliseconds < 30000) pump(20);
        if (!task.IsCompleted) throw new TimeoutException("Mod UI operation did not complete.");
        task.GetAwaiter().GetResult(); pump(30);
    }
    private static void Check(bool passed, string label) { if (!passed) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
}

