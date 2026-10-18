using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class CompactContentChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        using var fixture = new ModFixture(output); fixture.Add("alpha"); fixture.Add("bravo", disabled: true);
        using var vm = new InstanceModsViewModel("26.3", "Fabric", fixture.Root, fixture.Service);
        var view = new InstalledModsView { DataContext = vm };
        var host = new Window { Width = 960, Height = 600, Content = view }; host.Show(); pump(100);
        var selection = view.FindControl<WrapPanel>("ModSelectionActions")!;
        Check(!selection.IsEffectivelyVisible, "bulk mod actions are absent until a mod is selected");
        Check(!Visible(view).Any(b => b.Content as string is "Backups" or "Refresh" or "Folder" or "Page" or "Remove" or "Check mods" or "Select shown"), "rare actions and repeated row buttons no longer clutter the mods page");
        var more = view.FindControl<Button>("ModsMoreActions")!;
        Open(more, pump);
        var tools = (MenuFlyout)more.ContextFlyout!;
        Check(tools.Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, vm.CheckModsCommand)) && tools.Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, vm.ShowUpdatesCommand)), "mod check and updates remain available through More");
        var check = tools.Items.OfType<MenuItem>().Single(i => ReferenceEquals(i.Command, vm.CheckModsCommand));
        vm.IsBusy = true; pump(30); Check(!check.IsEnabled, "open tool menus follow busy-state changes"); vm.IsBusy = false; pump(30); Check(check.IsEnabled, "tool menus re-enable when idle");
        tools.Hide();
        vm.InstalledMods[0].IsSelected = true; pump(50);
        Check(selection.IsEffectivelyVisible && Visible(view).Any(b => b.Content as string == "Remove selected"), "selecting a checkbox reveals contextual bulk actions");
        foreach (var action in selection.Children.OfType<Button>()) Fits(action, host);
        vm.ClearSelectionCommand.Execute(null); pump(50); Check(!selection.IsEffectivelyVisible, "clearing selection hides the bulk toolbar again");
        var row = view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is ModModel && b.Content as string == "⋯");
        Open(row, pump); var rowMenu = (MenuFlyout)row.ContextFlyout!;
        var remove = rowMenu.Items.OfType<MenuItem>().Single(i => ReferenceEquals(i.Command, vm.RemoveModCommand));
        Check(ReferenceEquals(remove.CommandParameter, row.DataContext), "row menu targets exactly the chosen mod");
        vm.CanChangeFiles = () => false; vm.NotifyAvailability(); pump(30); Check(!remove.IsEnabled, "destructive row actions stay disabled for locked instances");
        vm.CanChangeFiles = () => true; vm.NotifyAvailability(); rowMenu.Hide();
        Open(more, pump); ((MenuFlyout)more.ContextFlyout!).Hide(); // Reopening does not create a second automatic button flyout.
        Check(more.Flyout == null, "More buttons do not also auto-open a second flyout");
        host.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact-mods.png"));
        vm.ShowUpdatesCommand.Execute(null); pump(50);
        Check(Visible(view).Any(b => b.Content as string == "← Installed mods"), "updates have one clear route back to the installed list");
        host.Close();

        foreach (string section in new[] { "Resource packs", "Shader packs", "Worlds" })
        {
            string directory = section == "Resource packs" ? "resourcepacks" : section == "Shader packs" ? "shaderpacks" : "saves";
            Directory.CreateDirectory(Path.Combine(fixture.Root, directory));
            if (section == "Worlds") Directory.CreateDirectory(Path.Combine(fixture.Root, directory, "Example world"));
            else File.WriteAllText(Path.Combine(fixture.Root, directory, "example.zip"), "fixture");
            var instance = new InstanceModel { Name = "Compact fixture", InstancePath = fixture.Root, Version = "26.3", Loader = "Fabric", IsInstalled = true };
            using var details = new InstanceDetailsViewModel(instance) { SelectedSection = section };
            using var files = new InstanceFilesView { DataContext = details };
            var window = new Window { Content = files, Width = 760, Height = 500 }; window.Show(); pump(100);
            Check(!Visible(files).Any(b => b.Content as string is "Add files" or "Refresh" or "Open folder" or "Open" or "Remove"), section + " uses the same compact action hierarchy");
            var button = files.FindControl<Button>("FilesMoreActions")!; Open(button, pump);
            Check(((MenuFlyout)button.ContextFlyout!).Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, details.RefreshCommand)), section + " retains refresh in its menu");
            button.ContextFlyout!.Hide();
            var fileRow = files.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is InstanceContentItem && b.Content as string == "⋯");
            Open(fileRow, pump);
            Check(((MenuFlyout)fileRow.ContextFlyout!).Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, details.RemoveItemCommand)) == details.IsInstallableSection, "worlds never gain a destructive content-removal action");
            fileRow.ContextFlyout!.Hide();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact-" + directory + ".png"));
            window.Close();
        }
        var catalogVm = new DownloadModsViewModel("26.3", "Fabric", fixture.Root, "mod", autoSearch: false);
        var catalogView = new ContentCatalogView { DataContext = catalogVm };
        var catalogHost = new Window { Content = catalogView, Width = 960, Height = 500 }; catalogHost.Show(); pump(80);
        Check(!Visible(catalogView).Any(b => b.Content as string is "Reset filters" or "Clear"), "catalog footer retains installation without permanent reset buttons");
        var catalogMore = catalogView.FindControl<Button>("CatalogMoreActions")!; Open(catalogMore, pump);
        var catalogMenu = (MenuFlyout)catalogMore.ContextFlyout!;
        Check(catalogMenu.Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, catalogVm.ClearSelectionCommand)) && catalogMenu.Items.OfType<MenuItem>().Any(i => ReferenceEquals(i.Command, catalogVm.ResetFiltersCommand)), "catalog selection and filter reset remain available in More");
        catalogVm.IsInstalling = true; pump(30);
        Check(catalogMenu.Items.OfType<MenuItem>().All(i => !i.IsEnabled), "catalog menus cannot reset state during installation");
        catalogVm.IsInstalling = false; catalogMenu.Hide(); catalogHost.Close();
        Console.WriteLine("COMPACT_CONTENT_OK");
    }
    private static Button[] Visible(Control root) => root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
    private static void Open(Button button, Action<int> pump) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(60); }
    private static void Fits(Control action, Window window)
    {
        var p = action.TranslatePoint(default, window)!.Value;
        Check(p.X >= 0 && p.X + action.Bounds.Width <= window.ClientSize.Width && p.Y + action.Bounds.Height <= window.ClientSize.Height, "contextual selection actions fit the small content view");
    }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
}
