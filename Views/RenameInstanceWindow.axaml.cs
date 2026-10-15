using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VortexLauncher.Views;

public partial class RenameInstanceWindow : Window
{
    public RenameInstanceWindow() : this(string.Empty) { }

    public RenameInstanceWindow(string name)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        UiMotion.AttachDialog(this, dialogContent);
        NameInput.Text = name;
        Opened += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter) { SaveName(); e.Handled = true; }
        };
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (SaveButton != null) SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameInput.Text);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnSave(object? sender, RoutedEventArgs e) => SaveName();
    private void SaveName()
    {
        if (!string.IsNullOrWhiteSpace(NameInput.Text)) Close(NameInput.Text.Trim());
    }
}
