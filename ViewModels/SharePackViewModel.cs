using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;

namespace VortexLauncher.ViewModels;

public partial class SharePackViewModel : ObservableObject, IDisposable
{
    private readonly InstanceModel _instance;
    private SharePackPreview? _preview;
    private CancellationTokenSource? _cancellation;
    [ObservableProperty] private bool _mods = true;
    [ObservableProperty] private bool _resourcePacks = true;
    [ObservableProperty] private bool _shaders = true;
    [ObservableProperty] private bool _config;
    [ObservableProperty] private bool _worlds;
    [ObservableProperty] private bool _screenshots;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Choose what to include, then build a preview. No files are uploaded.";
    [ObservableProperty] private string[] _files = Array.Empty<string>();
    public string Title => _instance.Name + " · " + _instance.Version + " · " + _instance.Loader;
    public bool CanEdit => !IsBusy && _instance.CanChangeFiles;
    public bool CanExport => CanEdit && _preview != null;
    public string Summary => _preview == null ? "Preview required" : $"{_preview.Files.Count} file(s) · {_preview.Bytes / 1048576d:F1} MB before compression";
    public SharePackViewModel(InstanceModel instance) { _instance = instance; _instance.PropertyChanged += OnInstanceChanged; }
    private void OnInstanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstanceModel.CanChangeFiles)) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanExport)); }
    }
    private void Invalidate() { _preview = null; Files = Array.Empty<string>(); OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(Summary)); }
    partial void OnModsChanged(bool value) => Invalidate();
    partial void OnResourcePacksChanged(bool value) => Invalidate();
    partial void OnShadersChanged(bool value) => Invalidate();
    partial void OnConfigChanged(bool value) => Invalidate();
    partial void OnWorldsChanged(bool value) => Invalidate();
    partial void OnScreenshotsChanged(bool value) => Invalidate();
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanExport)); }
    [RelayCommand] private void CancelOperation() => _cancellation?.Cancel();
    [RelayCommand] private async Task PreviewAsync()
    {
        if (!CanEdit) return;
        Invalidate(); IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        var options = new SharePackOptions(Mods, ResourcePacks, Shaders, Config, Worlds, Screenshots);
        try { Status = "Checking selected files…"; _preview = await Task.Run(() => SharePackService.Preview(_instance, options, cancellation.Token)); Files = _preview.Files.Select(f => f.RelativePath).ToArray(); Status = "Review the file list. Logs, caches, server lists and launcher accounts are excluded."; }
        catch (OperationCanceledException) { Status = "Preview cancelled. No ZIP created."; }
        catch (Exception ex) { Status = "Could not preview: " + ex.Message; }
        finally { _cancellation = null; IsBusy = false; OnPropertyChanged(nameof(Summary)); }
    }
    public async Task ExportAsync(string destination)
    {
        if (!CanExport || _preview == null) return;
        var preview = _preview; IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        using var operation = DownloadCenter.Begin("Share pack: " + _instance.Name, cancellation.Cancel);
        var progress = new Progress<string>(s => { Status = s; operation.Report(s); });
        try { await Task.Run(() => SharePackService.Export(_instance, preview, destination, cancellation.Token, progress)); Status = "Shared ZIP saved: " + destination + ". Send this file to your friend; nothing was uploaded."; operation.Complete(Status); }
        catch (OperationCanceledException) { Status = "Export cancelled. No ZIP published."; operation.Cancelled(); }
        catch (Exception ex) { Status = "Could not share: " + ex.Message; operation.Fail(ex); }
        finally { _cancellation = null; IsBusy = false; }
    }
    public void Dispose() { _instance.PropertyChanged -= OnInstanceChanged; _cancellation?.Cancel(); }
}
