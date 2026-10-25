using Avalonia.Controls;
using Avalonia.Input;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class DownloadModsWindow : Window
{
    public DownloadModsWindow()
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        UiMotion.AttachDialog(this, dialogContent);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DownloadModsViewModel vm)
                vm.ConfirmDependenciesAsync = dependencies => new DependencyApprovalWindow(dependencies).ShowDialog<bool>(this);
        };
        Closed += (_, _) => (DataContext as DownloadModsViewModel)?.CancelSearch();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            // Проверяем, что скролл приблизился к нижнему краю
            if (scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 50)
            {
                if (DataContext is DownloadModsViewModel vm)
                {
                    _ = vm.LoadMoreModsAsync();
                }
            }
        }
    }

    private void OnDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => new DownloadsWindow().Show(this);

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not DownloadModsViewModel vm || vm.IsInstalling) return;
        e.Handled = true;
        await vm.SearchModsAsync();
    }
}
