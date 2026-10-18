using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Views;

public partial class DependencyApprovalWindow : Window
{
    public DependencyApprovalWindow() : this(System.Array.Empty<ModInstallEntry>()) { }
    public DependencyApprovalWindow(IReadOnlyList<ModInstallEntry> dependencies)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = dependencies;
        UiMotion.AttachDialog(this, dialogContent);
        Opened += (_, _) => CancelButton.Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } };
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnApprove(object? sender, RoutedEventArgs e) => Close(true);
}
