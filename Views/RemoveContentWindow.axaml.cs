using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VortexLauncher.Views;

public partial class RemoveContentWindow : Window
{
    public RemoveContentWindow() : this(string.Empty) { }
    public RemoveContentWindow(string name)
    {
        InitializeComponent();
        FileNameLabel.Text = name;
        WindowTitleBar.Apply(this);
        UiMotion.AttachDialog(this, dialogContent);
        Opened += (_, _) => CancelButton.Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } };
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnRemove(object? sender, RoutedEventArgs e) => Close(true);
}
