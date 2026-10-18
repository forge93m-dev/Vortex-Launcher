using Avalonia.Controls;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Views;

public partial class DownloadsWindow : Window
{
    public DownloadsWindow()
    {
        InitializeComponent();
        tasks.ItemsSource = DownloadCenter.Operations;
        void Refresh() => emptyHint.IsVisible = DownloadCenter.Operations.Count == 0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Refresh();
        DownloadCenter.Operations.CollectionChanged += changed;
        Closed += (_, _) => DownloadCenter.Operations.CollectionChanged -= changed;
        Refresh();
        WindowTitleBar.Apply(this);
    }
}
