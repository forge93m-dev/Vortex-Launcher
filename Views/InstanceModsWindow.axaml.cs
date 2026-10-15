using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VortexLauncher.Views;

public partial class InstanceModsWindow : Window
{
    public InstanceModsWindow()
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.InstanceModsViewModel vm)
                vm.ConfirmRemovalAsync = name => new RemoveContentWindow(name).ShowDialog<bool>(this);
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
