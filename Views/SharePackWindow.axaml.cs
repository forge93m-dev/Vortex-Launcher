using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class SharePackWindow : Window
{
    private readonly InstanceModel _instance;
    public SharePackWindow() : this(new InstanceModel()) { }
    public SharePackWindow(InstanceModel instance)
    {
        _instance = instance; InitializeComponent(); WindowTitleBar.Apply(this);
        var model = new SharePackViewModel(instance); DataContext = model;
        Closed += (_, _) => model.Dispose();
    }
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SharePackViewModel { CanExport: true } model) return;
        string name = string.Concat(_instance.Name.Split(Path.GetInvalidFileNameChars()));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save shared pack", SuggestedFileName = (string.IsNullOrWhiteSpace(name) ? "Vortex pack" : name) + "-shared.zip",
            FileTypeChoices = new[] { new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } } }
        });
        if (file?.TryGetLocalPath() is { } path) await model.ExportAsync(path);
    }
}
