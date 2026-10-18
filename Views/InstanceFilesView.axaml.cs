using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class InstanceFilesView : UserControl, IDisposable
{
    private InstanceDetailsViewModel? _model;
    public Action? BrowseRequested { get; set; }
    public InstanceFilesView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Observe();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver); AddHandler(DragDrop.DropEvent, OnDrop);
    }
    private void Observe()
    {
        if (_model != null) { _model.Items.CollectionChanged -= OnItems; _model.PropertyChanged -= OnState; }
        _model = DataContext as InstanceDetailsViewModel;
        if (_model != null) { _model.Items.CollectionChanged += OnItems; _model.PropertyChanged += OnState; }
        Filter();
    }
    private void OnItems(object? sender, NotifyCollectionChangedEventArgs e) => Filter();
    private void OnState(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(InstanceDetailsViewModel.SelectedSection)) { FileSearch.Text = ""; Filter(); } }
    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Filter();
    private void Filter()
    {
        if (FileList == null || EmptyFiles == null) return;
        var files = _model?.Items.Where(f => f.Name.Contains(FileSearch.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray() ?? Array.Empty<InstanceContentItem>();
        FileList.ItemsSource = files; EmptyFiles.IsVisible = files.Length == 0 && _model?.IsServersSection != true;
        EmptyFiles.Text = string.IsNullOrEmpty(FileSearch.Text) ? _model?.EmptyDescription ?? "Nothing here yet." : "No matching files. Try another name.";
    }
    private void OnBrowse(object? sender, RoutedEventArgs e) => BrowseRequested?.Invoke();
    private void OnMoreActions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button anchor && _model != null)
            ContentActionMenus.Show(anchor, ContentActionMenus.Files(_model, () => OnAddFiles(this, new RoutedEventArgs())));
    }
    private void OnFileActions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstanceContentItem file } anchor && _model != null)
            ContentActionMenus.Show(anchor, ContentActionMenus.File(_model, file));
    }
    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        try { await AddFilesAsync(); }
        catch (Exception ex) { if (_model != null) _model.StatusMessage = "Could not add files: " + ex.Message; }
    }
    public async Task AddFilesAsync()
    {
        if (_model?.CanImportFiles != true || TopLevel.GetTopLevel(this) is not Window owner) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add files", AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType("Minecraft content") { Patterns = new[] { _model.IsModsSection ? "*.jar" : "*.zip" } } } });
        await _model.ImportFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private void OnDragOver(object? sender, DragEventArgs e) { e.DragEffects = _model?.CanImportFiles == true && e.Data.GetFiles()?.Any(f => f.TryGetLocalPath() is string path && _model.AcceptsFile(path)) == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void OnDrop(object? sender, DragEventArgs e) { e.Handled = true; if (_model != null) await _model.ImportFilesAsync(e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? Array.Empty<string>()); }
    public void Dispose() { if (_model != null) { _model.Items.CollectionChanged -= OnItems; _model.PropertyChanged -= OnState; } }
}
