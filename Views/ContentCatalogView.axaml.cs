using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class ContentCatalogView : UserControl
{
    public ContentCatalogView() { InitializeComponent(); CatalogResults.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged); }
    private void OnMoreActions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button anchor && DataContext is DownloadModsViewModel vm)
            ContentActionMenus.Show(anchor, ContentActionMenus.Catalog(vm));
    }
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer s && s.Offset.Y >= s.Extent.Height - s.Viewport.Height - 50 && DataContext is DownloadModsViewModel vm) _ = vm.LoadMoreModsAsync();
    }
    private void OnDownloadsClick(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window owner) new DownloadsWindow().Show(owner); }
    private void OnCancelInstall(object? sender, RoutedEventArgs e) { if (DataContext is DownloadModsViewModel vm) vm.CancelInstall(); }
    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not DownloadModsViewModel vm || vm.IsInstalling) return;
        e.Handled = true; await vm.SearchModsAsync();
    }
}
