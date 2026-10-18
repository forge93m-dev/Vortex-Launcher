using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

// Shared menus keep the embedded and standalone content managers consistent.
internal static class ContentActionMenus
{
    private static MenuItem Command(string title, ICommand command, object model, string? availability = null, object? parameter = null)
    {
        var item = new MenuItem { Header = title, Command = command, CommandParameter = parameter };
        if (availability != null) item.Bind(MenuItem.IsEnabledProperty, Availability(model, availability));
        return item;
    }
    private static MenuItem Action(string title, Action action, object model, string? availability = null)
    {
        var item = new MenuItem { Header = title };
        if (availability != null) item.Bind(MenuItem.IsEnabledProperty, Availability(model, availability));
        item.Click += (_, _) => action(); return item;
    }
    private static Binding Availability(object model, string path) => new(path.TrimStart('!')) { Source = model, Converter = path.StartsWith('!') ? BoolConverters.Not : null };
    internal static void Show(Button anchor, MenuFlyout menu) { anchor.ContextFlyout = menu; menu.ShowAt(anchor); }
    internal static MenuFlyout Mods(InstanceModsViewModel vm, Action? addFiles)
    {
        var menu = new MenuFlyout();
        if (addFiles != null) menu.Items.Add(Action("Add local files…", addFiles, vm, nameof(vm.CanMutate)));
        menu.Items.Add(Command("Check mods", vm.CheckModsCommand, vm, "!IsBusy"));
        menu.Items.Add(Command("Mod updates…", vm.ShowUpdatesCommand, vm, "!IsBusy"));
        menu.Items.Add(Command("Select all shown", vm.SelectVisibleCommand, vm, "!IsBusy"));
        menu.Items.Add(Action("Refresh list", vm.ReloadAfterContentChange, vm, "!IsBusy"));
        menu.Items.Add(Command("Open mods folder", vm.OpenModsFolderCommand, vm));
        menu.Items.Add(Command("Update backups", vm.OpenBackupsFolderCommand, vm));
        return menu;
    }
    internal static MenuFlyout Mod(InstanceModsViewModel vm, ModModel mod)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Command("Open verified mod page", vm.OpenModPageCommand, vm, "!IsBusy", mod));
        var remove = Command("Remove…", vm.RemoveModCommand, vm, nameof(vm.CanMutate), mod); remove.Classes.Add("danger"); menu.Items.Add(remove);
        return menu;
    }
    internal static MenuFlyout Files(InstanceDetailsViewModel vm, Action addFiles)
    {
        var menu = new MenuFlyout();
        if (vm.IsInstallableSection) menu.Items.Add(Action("Add local files…", addFiles, vm, nameof(vm.CanImportFiles)));
        menu.Items.Add(Command("Refresh list", vm.RefreshCommand, vm));
        menu.Items.Add(Command("Open folder", vm.OpenSectionFolderCommand, vm));
        return menu;
    }
    internal static MenuFlyout File(InstanceDetailsViewModel vm, InstanceContentItem file)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Command("Open", vm.OpenItemCommand, vm, parameter: file));
        if (vm.IsInstallableSection)
        {
            var remove = Command("Remove…", vm.RemoveItemCommand, vm, nameof(vm.CanRemoveFiles), file); remove.Classes.Add("danger"); menu.Items.Add(remove);
        }
        return menu;
    }
    internal static MenuFlyout Catalog(DownloadModsViewModel vm)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Command("Clear selection", vm.ClearSelectionCommand, vm, "!IsInstalling"));
        menu.Items.Add(Command("Reset filters", vm.ResetFiltersCommand, vm, "!IsInstalling"));
        return menu;
    }
}
