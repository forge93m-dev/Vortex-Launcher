using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class InstanceModsViewModel : ObservableObject, IDisposable
{
    private readonly string _gameVersion, _loader, _instancePath, _modsDir;
    private readonly string? _loaderVersion;
    public Func<ModPreflightReport, Task>? ShowModCheckAsync { get; set; }
    private readonly ModUpdateService _updates;
    private ModUpdatePreview? _preview;
    private CancellationTokenSource? _cancellation;
    public Func<bool>? CanChangeFiles { get; set; }
    public Func<Task>? BrowseContentAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmRemovalAsync { get; set; }
    public Func<int, Task<bool>>? ConfirmUpdatesAsync { get; set; }
    public Func<Window>? DialogOwner { get; set; }
    public Action<string>? OpenExternalUrl { get; set; } = Core.Launch.PlatformSupport.OpenPath;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPreviewVisible;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private ModModel? _selectedMod;
    public ObservableCollection<ModModel> InstalledMods { get; } = new();
    public ObservableCollection<ModModel> VisibleMods { get; } = new();
    public ObservableCollection<ModUpdateModel> AvailableUpdates { get; } = new();
    public IReadOnlyList<string> Filters { get; } = new[] { "All", "Enabled", "Disabled", "Selected" };
    public bool IsEmpty => InstalledMods.Count == 0;
    public bool NoMatches => !IsEmpty && VisibleMods.Count == 0;
    public bool NoUpdates => AvailableUpdates.Count == 0;
    public bool CanMutate => !IsBusy && CanChangeFiles?.Invoke() != false;
    public int SelectedCount => InstalledMods.Count(m => m.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public int SelectedUpdateCount => AvailableUpdates.Count(m => m.IsSelected);
    public string SelectionLabel => SelectedCount == 0 ? $"{VisibleMods.Count} of {InstalledMods.Count} mods" : $"{SelectedCount} selected · {VisibleMods.Count} shown / {InstalledMods.Count}";
    public string UpdateSelectionLabel => $"{SelectedUpdateCount} selected update(s)";
    public bool CanApplyUpdates => CanMutate && SelectedUpdateCount > 0 && _preview != null;
    public string CompatibilityLabel => $"Minecraft {_gameVersion} · {_loader}";
    public bool HasUndoRemoval => RemovalUndoHistory.HasUndo(_instancePath);
    public bool CanUndoRemoval => HasUndoRemoval && CanMutate;
    public bool CanEditSelection => CanMutate && SelectedCount > 0;
    public void NotifyAvailability() { OnPropertyChanged(nameof(CanMutate)); OnPropertyChanged(nameof(CanApplyUpdates)); OnPropertyChanged(nameof(CanUndoRemoval)); OnPropertyChanged(nameof(CanEditSelection)); }
    public void ReloadAfterContentChange() { InvalidatePreview(); RefreshInstalledMods(); }
    private void OnUndoChanged(string root) { if (RemovalUndoHistory.Matches(_instancePath, root)) { OnPropertyChanged(nameof(HasUndoRemoval)); OnPropertyChanged(nameof(CanUndoRemoval)); InvalidatePreview(); RefreshInstalledMods(); } }
    [RelayCommand] private void UndoRemoval()
    {
        if (!CanUndoRemoval) return;
        try { int count = RemovalUndoHistory.UndoLast(_instancePath, () => CanChangeFiles?.Invoke() != false && !IsBusy); StatusMessage = $"Restored {count} file(s)."; }
        catch (Exception ex) { StatusMessage = "Could not undo removal: " + ex.Message; }
        finally { InvalidatePreview(); RefreshInstalledMods(); }
    }

    public InstanceModsViewModel(string gameVersion, string loader, string instancePath, ModUpdateService? updates = null, string? loaderVersion = null)
    {
        _gameVersion = gameVersion; _loader = loader; _instancePath = instancePath;
        _modsDir = Path.Combine(instancePath, "mods"); _updates = updates ?? new();
        _loaderVersion = loaderVersion;
        RemovalUndoHistory.Changed += OnUndoChanged;
        InstalledMods.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null) foreach (ModModel m in e.OldItems) m.PropertyChanged -= OnModChanged;
            if (e.NewItems != null) foreach (ModModel m in e.NewItems) m.PropertyChanged += OnModChanged;
            FilterMods(); OnPropertyChanged(nameof(IsEmpty)); NotifySelection();
        };
        RefreshInstalledMods();
    }
    partial void OnSearchQueryChanged(string value) => FilterMods();
    partial void OnSelectedFilterChanged(string value) => FilterMods();
    partial void OnIsBusyChanged(bool value) => NotifyAvailability();
    private void OnModChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ModModel.IsSelected)) { if (SelectedFilter == "Selected") FilterMods(); NotifySelection(); } }
    private void NotifySelection() { OnPropertyChanged(nameof(SelectedCount)); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectionLabel)); OnPropertyChanged(nameof(CanEditSelection)); }
    private void FilterMods()
    {
        VisibleMods.Clear();
        foreach (var item in InstalledMods.Where(m => (m.FileName.Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) || m.Name.Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (SelectedFilter switch { "Enabled" => m.IsEnabled, "Disabled" => !m.IsEnabled, "Selected" => m.IsSelected, _ => true }))) VisibleMods.Add(item);
        OnPropertyChanged(nameof(NoMatches)); OnPropertyChanged(nameof(SelectionLabel));
    }
    public void RefreshInstalledMods()
    {
        var selected = InstalledMods.Where(m => m.IsSelected).Select(m => m.FilePath).ToHashSet();
        foreach (var mod in InstalledMods) mod.PropertyChanged -= OnModChanged;
        InstalledMods.Clear();
        try
        {
            ModFiles.RejectLinks(_modsDir);
            foreach (string file in Directory.Exists(_modsDir) ? Directory.GetFiles(_modsDir).Where(ModFiles.IsMod).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>())
                InstalledMods.Add(new() { Name = Path.GetFileNameWithoutExtension(ModFiles.EnabledName(file)), FileName = Path.GetFileName(file),
                    FilePath = file, IsEnabled = !ModFiles.IsDisabled(file), IsSelected = selected.Contains(file) });
        }
        catch (Exception ex) { StatusMessage = "Could not read mods: " + ex.Message; }
    }
    [RelayCommand] private void SelectVisible() { foreach (var item in VisibleMods.ToArray()) item.IsSelected = true; }
    [RelayCommand] private async Task CheckModsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        StatusMessage = "Checking mods locally…";
        try
        {
            var report = await new ModPreflightService().CheckAsync(_instancePath, _gameVersion, _loader, _loaderVersion, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = report.Counts;
            if (ShowModCheckAsync != null) await ShowModCheckAsync(report);
            else if (DialogOwner != null) await new ModCheckWindow(report, _instancePath).ShowDialog(DialogOwner());
        }
        catch (OperationCanceledException) { StatusMessage = "Mod check cancelled."; }
        catch (Exception ex) { StatusMessage = "Mod check failed: " + ex.Message; }
        finally { _cancellation = null; IsBusy = false; }
    }
    [RelayCommand] private void ClearSelection() { foreach (var item in InstalledMods) item.IsSelected = false; }
    [RelayCommand] private void ShowInstalled() => IsPreviewVisible = false;
    [RelayCommand] private void ShowUpdates() => IsPreviewVisible = true;
    [RelayCommand] private void CancelOperation() => _cancellation?.Cancel();
    [RelayCommand] private async Task OpenModPageAsync(ModModel? mod)
    {
        if (mod == null || !InstalledMods.Contains(mod) || IsBusy) return;
        IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        try
        {
            string? url = await Task.Run(() => _updates.GetProjectUrlAsync(_instancePath, mod.FilePath, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if (url == null) StatusMessage = "This file has no verified Modrinth page (or is the bundled Vortex Client). No page was guessed.";
            else OpenExternalUrl?.Invoke(url);
        }
        catch (OperationCanceledException) { StatusMessage = "Page lookup cancelled."; }
        catch (Exception ex) { StatusMessage = "Could not identify mod page: " + ex.Message; }
        finally { _cancellation = null; IsBusy = false; }
    }
    private void InvalidatePreview()
    {
        _preview = null;
        foreach (var item in AvailableUpdates) item.PropertyChanged -= OnUpdateChanged;
        AvailableUpdates.Clear(); OnUpdateChanged(null, new(null));
    }
    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    { OnPropertyChanged(nameof(SelectedUpdateCount)); OnPropertyChanged(nameof(UpdateSelectionLabel)); OnPropertyChanged(nameof(CanApplyUpdates)); OnPropertyChanged(nameof(NoUpdates)); }
    [RelayCommand] private async Task CheckUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true; IsPreviewVisible = true; InvalidatePreview();
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        using var operation = DownloadCenter.Begin("Check mod updates: " + Path.GetFileName(_instancePath), cancellation.Cancel);
        var progress = new Progress<string>(s => { StatusMessage = s; operation.Report(s); });
        try
        {
            bool prereleases = IncludePrereleases;
            var preview = await Task.Run(() => _updates.CheckAsync(_instancePath, _gameVersion, _loader, prereleases, progress, cancellation.Token));
            _preview = preview;
            foreach (var item in preview.Updates) { item.PropertyChanged += OnUpdateChanged; AvailableUpdates.Add(item); }
            StatusMessage = $"{preview.Updates.Count} update(s). {preview.Unrecognized} unrecognized/client file(s) left untouched." +
                (preview.Errors.Count > 0 ? $" {preview.Errors.Count} check(s) failed: " + string.Join("; ", preview.Errors.Take(3)) : " No files changed.");
            operation.Complete(StatusMessage);
        }
        catch (OperationCanceledException) { operation.Cancelled(); StatusMessage = "Update check cancelled; no files changed."; }
        catch (Exception ex) { operation.Fail(ex); StatusMessage = "Could not check updates: " + ex.Message; }
        finally { _cancellation = null; IsBusy = false; OnUpdateChanged(null, new(null)); }
    }
    [RelayCommand] private async Task ApplyUpdatesAsync()
    {
        if (!CanApplyUpdates || _preview == null || ConfirmUpdatesAsync == null) return;
        var preview = _preview; var selected = AvailableUpdates.Where(i => i.IsSelected).ToArray();
        IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        try
        {
            if (!await ConfirmUpdatesAsync(selected.Length) || cancellation.IsCancellationRequested) return;
            if (CanChangeFiles?.Invoke() == false) { StatusMessage = "Instance is busy; no files changed."; return; }
            using var operation = DownloadCenter.Begin("Update mods: " + Path.GetFileName(_instancePath), cancellation.Cancel);
            var progress = new Progress<string>(s => { StatusMessage = s; operation.Report(s); });
            try
            {
                string backup = await Task.Run(() => _updates.ApplyAsync(preview, selected, () => CanChangeFiles?.Invoke() != false, progress, cancellation.Token));
                StatusMessage = $"Updated {selected.Length} mod(s). Old files kept in {backup}";
                operation.Complete(StatusMessage); InvalidatePreview(); RefreshInstalledMods();
            }
            catch (OperationCanceledException) { operation.Cancelled(); StatusMessage = "Update cancelled; original mods restored."; InvalidatePreview(); RefreshInstalledMods(); }
            catch (Exception ex) { operation.Fail(ex); StatusMessage = "Update stopped: " + ex.Message; InvalidatePreview(); RefreshInstalledMods(); }
        }
        finally { _cancellation = null; IsBusy = false; }
    }
    [RelayCommand] private async Task OpenDownloadModsAsync()
    {
        if (!CanMutate) return;
        if (BrowseContentAsync != null) { await BrowseContentAsync(); return; }
        IsBusy = true;
        try
        {
            var window = new DownloadModsWindow { DataContext = new DownloadModsViewModel(_gameVersion, _loader, _instancePath) };
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                await window.ShowDialog(DialogOwner?.Invoke() ?? owner);
            InvalidatePreview(); RefreshInstalledMods();
        }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void ToggleMod(ModModel? mod)
    {
        if (mod == null || !InstalledMods.Contains(mod) || !CanMutate) return;
        ChangeEnabled(new[] { mod }, ModFiles.IsDisabled(mod.FilePath));
    }
    [RelayCommand] private void EnableSelected() { if (CanMutate) ChangeEnabled(InstalledMods.Where(m => m.IsSelected).ToArray(), true); }
    [RelayCommand] private void DisableSelected() { if (CanMutate) ChangeEnabled(InstalledMods.Where(m => m.IsSelected).ToArray(), false); }
    private void ChangeEnabled(ModModel[] mods, bool enabled)
    {
        int changed = 0; var errors = new List<string>();
        foreach (var mod in mods)
        {
            if (CanChangeFiles?.Invoke() == false) { errors.Add("Instance became busy."); break; }
            try { ModFiles.SetEnabled(_instancePath, mod.FilePath, enabled); changed++; }
            catch (Exception ex) { errors.Add(mod.FileName + ": " + ex.Message); }
        }
        InvalidatePreview(); RefreshInstalledMods(); StatusMessage = $"Processed {changed} mod(s). " + string.Join("; ", errors.Take(3));
    }
    [RelayCommand] private async Task RemoveModAsync(ModModel? mod)
    { if (mod != null && InstalledMods.Contains(mod)) await RemoveAsync(new[] { mod }); }
    [RelayCommand] private Task RemoveSelectedAsync() => RemoveAsync(InstalledMods.Where(m => m.IsSelected).ToArray());
    private async Task RemoveAsync(ModModel[] mods)
    {
        if (mods.Length == 0 || !CanMutate || ConfirmRemovalAsync == null) return;
        IsBusy = true;
        try
        {
            string names = mods.Length == 1 ? mods[0].FileName : $"{mods.Length} selected mods: " + string.Join(", ", mods.Take(5).Select(m => m.FileName));
            if (!await ConfirmRemovalAsync(names)) return;
            int removed = 0;
            var undo = new List<RemovedContent>();
            try
            {
            foreach (var mod in mods)
            {
                if (CanChangeFiles?.Invoke() == false || !InstalledMods.Contains(mod)) break;
                ModFiles.Validate(_instancePath, mod.FilePath);
                string backup = InstanceContentRemoval.MoveToBackup(_instancePath, "Mods", mod.FilePath); undo.Add(new("Mods", backup)); removed++;
            }
            }
            finally { RemovalUndoHistory.Remember(_instancePath, undo); }
            StatusMessage = $"Removed {removed} mod(s). Use Undo removal to restore them.";
            InvalidatePreview(); RefreshInstalledMods();
        }
        catch (Exception ex) { StatusMessage = "Could not remove file: " + ex.Message; InvalidatePreview(); RefreshInstalledMods(); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void OpenModsFolder() { try { if (Directory.Exists(_modsDir)) Core.Launch.PlatformSupport.OpenPath(_modsDir); } catch (Exception ex) { StatusMessage = ex.Message; } }
    [RelayCommand] private void OpenBackupsFolder() { try { string path = Path.Combine(_instancePath, ".vortex-mod-updates"); if (Directory.Exists(path)) Core.Launch.PlatformSupport.OpenPath(path); } catch (Exception ex) { StatusMessage = ex.Message; } }
    public void Dispose() { RemovalUndoHistory.Changed -= OnUndoChanged; _cancellation?.Cancel(); foreach (var mod in InstalledMods) mod.PropertyChanged -= OnModChanged; foreach (var update in AvailableUpdates) update.PropertyChanged -= OnUpdateChanged; ConfirmRemovalAsync = null; ConfirmUpdatesAsync = null; ShowModCheckAsync = null; DialogOwner = null; OpenExternalUrl = null; }
}
