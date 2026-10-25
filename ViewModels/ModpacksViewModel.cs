using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Network;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.ViewModels;

public partial class ModpacksViewModel : ObservableObject
{
    private readonly ModrinthService _modrinthService = new();
    private readonly Func<string, Task> _importArchive;
    private readonly Action<bool, string, string> _setLoadingState;
    private CancellationTokenSource? _searchCancellation;
    private int _searchRequest;
    private bool _changingFilters;
    private string _activeQuery = "", _activeCategory = "", _activeLoader = "";
    private string _activeSort = "relevance";

    public IReadOnlyList<ModSearchFilter> Categories { get; } = ContentSearchFilters.For("modpack");
    public IReadOnlyList<ModSearchFilter> SortOptions => ContentSearchFilters.SortOptions;
    public IReadOnlyList<ModSearchFilter> Loaders { get; } = new ModSearchFilter[]
    {
        new("", "All loaders"), new("fabric", "Fabric"), new("forge", "Forge"),
        new("quilt", "Quilt"), new("neoforge", "NeoForge")
    };

    [ObservableProperty] private ModSearchFilter _selectedCategory = new("", "All categories");
    [ObservableProperty] private ModSearchFilter _selectedSort = new("relevance", "Relevance");
    [ObservableProperty] private ModSearchFilter _selectedLoader = new("", "All loaders");
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _hasMore;

    partial void OnSelectedCategoryChanged(ModSearchFilter value) { if (!_changingFilters) _ = SearchModpacksAsync(); }
    partial void OnSelectedSortChanged(ModSearchFilter value) { if (!_changingFilters) _ = SearchModpacksAsync(); }
    partial void OnSelectedLoaderChanged(ModSearchFilter value) { if (!_changingFilters) _ = SearchModpacksAsync(); }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        if (IsInstalling) return;
        _changingFilters = true;
        try
        {
            SelectedCategory = Categories[0];
            SelectedSort = SortOptions[0];
            SelectedLoader = Loaders[0];
            SearchQuery = "";
        }
        finally { _changingFilters = false; }
        await SearchModpacksAsync();
    }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthModItemViewModel? _selectedModpack;

    [ObservableProperty]
    private ModrinthVersionItem? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "Search for modpacks...";

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isLoadingVersions;

    private int _versionRequest;

    public ObservableCollection<ModrinthModItemViewModel> Modpacks { get; } = new();
    public ObservableCollection<ModrinthVersionItem> ModpackVersions { get; } = new();

    public ModpacksViewModel(Func<string, Task> importArchive, Action<bool, string, string> setLoadingState)
    {
        _importArchive = importArchive;
        _setLoadingState = setLoadingState;
        _selectedCategory = Categories[0];
        _selectedSort = SortOptions[0];
        _selectedLoader = Loaders[0];
        _ = SearchModpacksAsync();
    }

    partial void OnSelectedModpackChanged(ModrinthModItemViewModel? value)
    {
        _versionRequest++;
        ModpackVersions.Clear();
        SelectedVersion = null;
        IsLoadingVersions = false;
        if (value != null)
        {
            _ = LoadModpackVersionsAsync(value.Item.ProjectId);
        }
        else
        {
            ModpackVersions.Clear();
            SelectedVersion = null;
        }
    }

    private async Task LoadModpackVersionsAsync(string projectId)
    {
        int request = _versionRequest;
        IsLoadingVersions = true;
        StatusMessage = "Loading available versions...";
        ModpackVersions.Clear();

        var versions = await _modrinthService.GetProjectVersionsAsync(projectId);
        if (request != _versionRequest) return;
        IsLoadingVersions = false;
        foreach (var v in versions.Where(v => string.IsNullOrEmpty(_activeLoader) ||
                     v.Loaders.Contains(_activeLoader, StringComparer.OrdinalIgnoreCase))
                 .OrderByDescending(v => v.DatePublished))
        {
            ModpackVersions.Add(v);
        }

        if (ModpackVersions.Count > 0)
        {
            SelectedVersion = ModpackVersions[0];
            StatusMessage = $"Selected {SelectedModpack?.Item.Title}";
        }
        else
        {
            StatusMessage = "No versions found for this modpack.";
        }
    }

    [RelayCommand]
    public async Task SearchModpacksAsync()
    {
        if (IsInstalling) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        int request = ++_searchRequest;
        var token = _searchCancellation.Token;
        _activeQuery = SearchQuery;
        _activeCategory = SelectedCategory.Id;
        _activeSort = SelectedSort.Id;
        _activeLoader = SelectedLoader.Id;
        IsSearching = true;
        HasMore = false;
        StatusMessage = "Searching Modrinth modpacks...";
        SelectedModpack = null;
        Modpacks.Clear();
        try
        {
            var result = await _modrinthService.SearchModpacksAsync(_activeQuery, 0, 20,
                _activeCategory, _activeSort, loader: _activeLoader, cancellationToken: token);
            if (request != _searchRequest) return;
            foreach (var pack in result.Hits) Modpacks.Add(new ModrinthModItemViewModel(pack));
            HasMore = Modpacks.Count < result.TotalHits;
            StatusMessage = result.Hits.Count == 0 ? "No results found. Try another category or reset the filters."
                : $"Loaded {Modpacks.Count} of {result.TotalHits} modpacks.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (request == _searchRequest) StatusMessage = $"Search failed: {ex.Message}"; }
        finally { if (request == _searchRequest) IsSearching = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsSearching || IsInstalling || _searchCancellation == null) return;
        int request = _searchRequest;
        var token = _searchCancellation.Token;
        IsSearching = true;
        try
        {
            var result = await _modrinthService.SearchModpacksAsync(_activeQuery, Modpacks.Count, 20,
                _activeCategory, _activeSort, loader: _activeLoader, cancellationToken: token);
            if (request != _searchRequest) return;
            foreach (var pack in result.Hits) Modpacks.Add(new ModrinthModItemViewModel(pack));
            HasMore = result.Hits.Count > 0 && Modpacks.Count < result.TotalHits;
            StatusMessage = $"Loaded {Modpacks.Count} of {result.TotalHits} modpacks.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (request == _searchRequest) StatusMessage = $"Could not load more: {ex.Message}"; }
        finally { if (request == _searchRequest) IsSearching = false; }
    }

    [RelayCommand]
    public async Task InstallSelectedModpackAsync()
    {
        if (SelectedModpack == null || SelectedVersion == null || IsInstalling) return;
        string name = SelectedModpack.Item.Title;
        var primary = SelectedVersion.Files.FirstOrDefault(f => f.Primary) ?? SelectedVersion.Files.FirstOrDefault();
        if (primary == null) { StatusMessage = "No modpack download is available."; return; }
        using var cancellation = new CancellationTokenSource();
        using var operation = VortexLauncher.Core.Management.DownloadCenter.Begin($"Modpack download: {name}", cancellation.Cancel);
        IsInstalling = true;
        string temporary = Path.Combine(Path.GetTempPath(), "vortex-pack-" + Guid.NewGuid().ToString("N") + ".mrpack");
        _setLoadingState(true, "Downloading modpack", name);
        try
        {
            var transfer = new TransferProgress(operation.Report);
            transfer.Begin("Modpack archive", 1, 0);
            primary.Hashes.TryGetValue("sha1", out string? hash);
            long received = 0;
            await DownloadCache.EnsureAsync(primary.Url, temporary, cancellation.Token, hash,
                received: bytes => { received += bytes; transfer.Received(bytes); });
            transfer.Complete(new FileInfo(temporary).Length, received);
            cancellation.Token.ThrowIfCancellationRequested();
            // Use the same validated import/preview pipeline as a locally chosen archive.
            // Never infer the destination from a pack name or overwrite an existing instance.
            _setLoadingState(false, "", "Review import preview");
            operation.Complete("Pack downloaded. Review the import preview before installing content.");
            await _importArchive(temporary);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            operation.Cancelled(); StatusMessage = "Modpack download cancelled.";
        }
        catch (Exception ex)
        {
            operation.Fail(ex); StatusMessage = "Modpack download failed: " + ex.Message;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            IsInstalling = false;
            _setLoadingState(false, "", StatusMessage);
        }
    }
}
