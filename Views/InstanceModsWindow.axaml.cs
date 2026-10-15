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
            {
                vm.DialogOwner = () => this;
                vm.ConfirmRemovalAsync = name => new RemoveContentWindow(name).ShowDialog<bool>(this);
                vm.ConfirmUpdatesAsync = count => new TaskDialog("Update selected mods?",
                    $"Replace {count} selected mod(s)? Original files will be kept in .vortex-mod-updates. Required dependencies are checked before changes. Disabled mods stay disabled.", "Update selected").ShowDialog<bool>(this);
            }
        };
        Closed += (_, _) => (DataContext as ViewModels.InstanceModsViewModel)?.Dispose();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
