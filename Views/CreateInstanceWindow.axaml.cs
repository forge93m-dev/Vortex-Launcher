using Avalonia.Controls;
using Avalonia.Interactivity;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class CreateInstanceWindow : Window
{
    public CreateInstanceWindow() : this(new CreateInstanceViewModel()) { }

    public CreateInstanceWindow(object viewModel)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        UiMotion.AttachDialog(this, dialogContent);
        DataContext = viewModel;
    }

    // A chip click picks that version. Chips follow the version box one way, so the box stays the single source of truth.
    private void OnQuickVersionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is string version && DataContext is IVersionChoice choice && choice.SelectedVersion != version)
            choice.SelectedVersion = version;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateInstanceViewModel vm)
        {
            Close(vm);
        }
        else
        {
            Close(null);
        }
    }
}
