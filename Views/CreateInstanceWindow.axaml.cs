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
