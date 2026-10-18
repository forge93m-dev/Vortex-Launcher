using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class QolUiChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        string root = Directory.CreateDirectory(Path.Combine(output, "qol-ui-fixture")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        string path = Path.Combine(root, "mods", "test.jar.disabled"); File.WriteAllText(path, "qol fixture");
        var item = new InstanceModel { Name = "QoL fixture", Version = "26.3", Loader = "Fabric", IsInstalled = true, InstancePath = root };
        var manager = new InstanceDetailsWindow(item) { Width = 860, Height = 600 }; manager.Show(); pump(250);
        var model = (InstanceDetailsViewModel)manager.DataContext!;
        using var other = new InstanceModsViewModel(item.Version, item.Loader, root);
        other.CanChangeFiles = () => item.CanChangeFiles;
        model.ConfirmRemovalAsync = _ => Task.FromResult(true);
        Finish(model.RemoveItemCommand.ExecuteAsync(model.Items.Single())); pump(40);
        Check(model.IsEmpty && other.IsEmpty && model.HasUndoRemoval && other.HasUndoRemoval, "removal updates both open content managers and exposes Undo");
        var undo = manager.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Undo removal"));
        Check(undo.IsEffectivelyVisible && undo.IsEnabled, "Undo button is visible/enabled after removal");
        manager.CaptureRenderedFrame()!.Save(Path.Combine(output, "qol-undo-minimum.png"));
        item.IsRunning = true; pump(20);
        Check(!undo.IsEnabled && !model.CanUndoRemoval, "Undo button tracks a running instance");
        item.IsRunning = false; other.UndoRemovalCommand.Execute(null); pump(30);
        Check(File.ReadAllText(path) == "qol fixture" && model.Items.Count == 1 && other.InstalledMods.Count == 1 && !model.HasUndoRemoval, "Undo in mod manager refreshes the other window and preserves disabled filename");
        var share = manager.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Share pack…"));
        Check(share.IsEffectivelyVisible, "share action is present in instance manager");
        manager.Close();
        var dialog = new SharePackWindow(item) { Width = 620, Height = 520 }; dialog.Show(); pump(220);
        var vm = (SharePackViewModel)dialog.DataContext!;
        Check(vm.Mods && vm.ResourcePacks && vm.Shaders && !vm.Config && !vm.Worlds && !vm.Screenshots && !vm.CanExport, "share UI requires preview and defaults private categories off");
        Finish(vm.PreviewCommand.ExecuteAsync(null)); pump(40);
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save shared ZIP…"));
        Check(vm.CanExport && vm.Files.SequenceEqual(new[] { "mods/test.jar.disabled" }) && save.IsEnabled, "preview command binds reviewed files and enables ZIP action");
        item.IsRunning = true; pump(20);
        Check(!save.IsEnabled && !vm.CanEdit, "share UI disables preview/export while the instance is running");
        item.IsRunning = false; pump(20);
        foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)) {
            var corner = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, button.Bounds.Height), dialog);
            Check(corner != null && corner.Value.X <= dialog.Bounds.Width && corner.Value.Y <= dialog.Bounds.Height, "share controls fit minimum window: " + button.Content);
        }
        dialog.CaptureRenderedFrame()!.Save(Path.Combine(output, "qol-share-minimum.png"));
        vm.Config = true; pump(30);
        Check(!vm.CanExport && !save.IsEnabled && vm.Files.Length == 0, "changing share options invalidates stale preview and disables Save");
        Finish(vm.PreviewCommand.ExecuteAsync(null));
        string zip = Path.Combine(output, "qol-shared.zip"); Finish(vm.ExportAsync(zip));
        Check(File.Exists(zip) && vm.Status.Contains("nothing was uploaded"), "shared ZIP is created locally without network upload");
        dialog.Width = 760; dialog.Height = 640; pump(220);
        dialog.CaptureRenderedFrame()!.Save(Path.Combine(output, "qol-share-default.png")); dialog.Close();
        Console.WriteLine("QOL_UI_OK");
        void Finish(Task task) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted && clock.Elapsed.TotalSeconds < 15) pump(20); if (!task.IsCompleted) throw new TimeoutException("QoL UI operation"); task.GetAwaiter().GetResult(); }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
}
