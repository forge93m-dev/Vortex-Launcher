using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VortexLauncher.Views;

public partial class InstalledModsView : UserControl
{
    public Action? OverviewRequested { get; set; }
    public Func<Task>? AddFilesRequested { get; set; }
    public InstalledModsView() => InitializeComponent();
    private void OnMoreActions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button anchor && DataContext is ViewModels.InstanceModsViewModel vm)
            ContentActionMenus.Show(anchor, ContentActionMenus.Mods(vm, AddFilesRequested == null ? null : () => OnAddFiles(this, new RoutedEventArgs())));
    }
    private void OnModActions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Models.ModModel mod } anchor && DataContext is ViewModels.InstanceModsViewModel vm)
            ContentActionMenus.Show(anchor, ContentActionMenus.Mod(vm, mod));
    }
    private void OnCloseClick(object? sender, RoutedEventArgs e) => OverviewRequested?.Invoke();
    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        try { if (AddFilesRequested != null) await AddFilesRequested(); }
        catch (Exception ex) { if (DataContext is ViewModels.InstanceModsViewModel vm) vm.StatusMessage = "Could not add files: " + ex.Message; }
    }
    private void OnRefresh(object? sender, RoutedEventArgs e) { if (DataContext is ViewModels.InstanceModsViewModel { IsBusy: false } vm) vm.ReloadAfterContentChange(); }
}
