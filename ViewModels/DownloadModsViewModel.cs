using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Network;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using System.Security.Cryptography;

namespace VortexLauncher.ViewModels;

public record ModSearchFilter(string Id, string Label);

public partial class ModrinthModItemViewModel : ObservableObject
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, Bitmap> ImageCache = new();

    public ModrinthModItem Item { get; }

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ModrinthVersionItem? _selectedVersion;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string _versionStatus = "Select an item to load versions.";

    public ObservableCollection<ModrinthVersionItem> AvailableVersions { get; } = new();
    internal Task? VersionLoadTask { get; set; }

    public event Action? SelectionChanged;

    public ModrinthModItemViewModel(ModrinthModItem item)
    {
        Item = item;
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        if (string.IsNullOrWhiteSpace(Item.IconUrl)) return;
        if (ImageCache.TryGetValue(Item.IconUrl, out var cached))
        {
            IconBitmap = cached;
            return;
        }

        try
        {
            byte[] data = await HttpClient.GetByteArrayAsync(Item.IconUrl);
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            ImageCache[Item.IconUrl] = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() => IconBitmap = bitmap);
        }
        catch { }
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}

public partial class DownloadModsViewModel : ObservableObject
{
    public Func<IReadOnlyList<ModInstallEntry>, Task<bool>>? ConfirmDependenciesAsync { get; set; }
    private readonly ModrinthService _modrinthService = new();
    private readonly string _gameVersion;
    private readonly string _loader;
    private readonly string _instancePath;
    private readonly string _projectType;
    private int _currentOffset;
    private const int PageSize = 20;
    private bool _hasMore = true;
    private bool _isLoadingMore;
    private int _searchRequest;
    private CancellationTokenSource? _searchCancellation;
    private readonly Dictionary<string, ModrinthModItemViewModel> _knownItems = new();
    private string _activeQuery = string.Empty;
    private string _activeCategory = string.Empty;
    private string _activeSort = "relevance";
    private bool _changingFilters;

    public bool IsModContent => _projectType == "mod";
    public string CompatibilityLabel => $"Minecraft {_gameVersion}" + (IsModContent ? $" · {_loader}" : string.Empty);
    public IReadOnlyList<ModSearchFilter> Categories { get; }
    public IReadOnlyList<ModSearchFilter> SortOptions => ContentSearchFilters.SortOptions;

    [ObservableProperty]
    private ModSearchFilter _selectedCategory = new("", "All categories");

    [ObservableProperty]
    private ModSearchFilter _selectedSort = new("relevance", "Relevance");

    [ObservableProperty]
    private bool _isSearching;

    partial void OnSelectedCategoryChanged(ModSearchFilter value) { if (!_changingFilters) _ = SearchModsAsync(); }
    partial void OnSelectedSortChanged(ModSearchFilter value) { if (!_changingFilters) _ = SearchModsAsync(); }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        if (IsInstalling) return;
        _changingFilters = true;
        try
        {
            SelectedCategory = Categories[0];
            SelectedSort = SortOptions[0];
            SearchQuery = string.Empty;
        }
        finally { _changingFilters = false; }
        await SearchModsAsync();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in AllKnownItems()) item.IsSelected = false;
        OnSelectionChanged();
    }

    private IEnumerable<ModrinthModItemViewModel> AllKnownItems() =>
        _knownItems.Values.Concat(SearchResults).Distinct();

    public void CancelSearch()
    {
        _searchRequest++;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        IsSearching = false;
    }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthModItemViewModel? _selectedMod;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;

    partial void OnSelectedModChanged(ModrinthModItemViewModel? value)
    {
        if (value != null) _ = EnsureVersionsAsync(value);
    }

    private Task EnsureVersionsAsync(ModrinthModItemViewModel item)
    {
        if (item.AvailableVersions.Count > 0) return Task.CompletedTask;
        if (item.VersionLoadTask is { IsCompleted: false }) return item.VersionLoadTask;
        return item.VersionLoadTask = LoadVersionsAsync(item);
    }

    private async Task LoadVersionsAsync(ModrinthModItemViewModel item)
    {
        item.IsLoadingVersions = true;
        item.VersionStatus = "Loading versions...";
        try
        {
            var versions = await _modrinthService.GetProjectVersionsAsync(item.Item.ProjectId);
            foreach (var version in versions.Where(IsCompatibleVersion).OrderByDescending(v => v.DatePublished))
                item.AvailableVersions.Add(version);
            item.SelectedVersion ??= item.AvailableVersions.FirstOrDefault();
            item.VersionStatus = item.AvailableVersions.Count > 0
                ? $"{item.AvailableVersions.Count} versions for Minecraft {_gameVersion}" +
                  (_projectType == "mod" ? $" / {_loader}" : string.Empty)
                : "No compatible versions found. Select this item again to retry.";
        }
        finally { item.IsLoadingVersions = false; }
    }

    private bool IsCompatibleVersion(ModrinthVersionItem version) =>
        version.GameVersions.Contains(_gameVersion, StringComparer.OrdinalIgnoreCase) &&
        (_projectType != "mod" || version.Loaders.Contains(_loader, StringComparer.OrdinalIgnoreCase)) &&
        version.Files.Any(f => !string.IsNullOrWhiteSpace(f.Url));

    public ObservableCollection<ModrinthModItemViewModel> SearchResults { get; } = new();
    public int SelectedCount => AllKnownItems().Count(mod => mod.IsSelected);
    public string ContentTitle => _projectType switch
    {
        "resourcepack" => "Resource packs",
        "shader" => "Shaders",
        _ => "Mods"
    };

    public DownloadModsViewModel(string gameVersion, string loader, string instancePath, string projectType = "mod", ModrinthService? service = null)
    {
        _modrinthService = service ?? new ModrinthService();
        _gameVersion = gameVersion;
        _loader = loader;
        _instancePath = instancePath;
        _projectType = projectType;
        Categories = ContentSearchFilters.For(projectType);
        _selectedCategory = Categories[0];
        _selectedSort = SortOptions[0];
        StatusMessage = $"Search for {ContentTitle.ToLowerInvariant()}...";
        _ = SearchModsAsync();
    }

    [RelayCommand]
    public async Task SearchModsAsync()
    {
        if (IsInstalling) return;
        CancelSearch();
        int request = _searchRequest;
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        _activeQuery = SearchQuery;
        _activeCategory = SelectedCategory.Id;
        _activeSort = SelectedSort.Id;
        IsSearching = true;
        _currentOffset = 0;
        _hasMore = true;
        StatusMessage = $"Searching {ContentTitle.ToLowerInvariant()}...";
        SearchResults.Clear();
        SelectedMod = null;

        try
        {
            var result = await _modrinthService.SearchModsAsync(
                _activeQuery, _gameVersion, _loader, 0, PageSize, _projectType,
                _activeCategory, _activeSort, token);
            if (request != _searchRequest) return;
            var hits = result?.Hits ?? new List<ModrinthModItem>();
            AddSearchResults(hits);
            _currentOffset += hits.Count;
            _hasMore = _currentOffset < (result?.TotalHits ?? 0);
            StatusMessage = hits.Count == 0 ? "No results found. Try another category or reset the filters."
                : $"Loaded {SearchResults.Count} of {result?.TotalHits ?? 0} {ContentTitle.ToLowerInvariant()}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (request != _searchRequest) return;
            StatusMessage = $"Search failed: {ex.Message}";
            _hasMore = false;
        }
        finally { if (request == _searchRequest) IsSearching = false; }
    }

    public async Task LoadMoreModsAsync()
    {
        if (!_hasMore || _isLoadingMore || IsSearching || IsInstalling || _searchCancellation == null) return;
        int request = _searchRequest;
        var token = _searchCancellation.Token;
        _isLoadingMore = true;
        try
        {
            var result = await _modrinthService.SearchModsAsync(
                _activeQuery, _gameVersion, _loader, _currentOffset, PageSize, _projectType,
                _activeCategory, _activeSort, token);
            if (request != _searchRequest) return;
            var hits = result?.Hits ?? new List<ModrinthModItem>();
            AddSearchResults(hits);
            _currentOffset += hits.Count;
            _hasMore = hits.Count > 0 && _currentOffset < (result?.TotalHits ?? 0);
            StatusMessage = $"Loaded {SearchResults.Count} of {result?.TotalHits ?? 0} {ContentTitle.ToLowerInvariant()}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (request == _searchRequest) StatusMessage = $"Could not load more: {ex.Message}";
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    private void AddSearchResults(IEnumerable<ModrinthModItem>? items)
    {
        if (items == null) return;
        foreach (var mod in items)
        {
            if (mod == null) continue;
            if (!_knownItems.TryGetValue(mod.ProjectId, out var item))
            {
                item = new ModrinthModItemViewModel(mod);
                item.SelectionChanged += OnSelectionChanged;
                _knownItems.Add(mod.ProjectId, item);
            }
            if (!SearchResults.Contains(item)) SearchResults.Add(item);
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void OnSelectionChanged() => OnPropertyChanged(nameof(SelectedCount));

    [RelayCommand]
    public async Task DownloadSelectedModAsync()
    {
        if (SelectedMod == null)
        {
            StatusMessage = "Select an item first.";
            return;
        }
        SelectedMod.IsSelected = true;
        await InstallSelectedModsAsync();
    }

    [RelayCommand]
    public async Task InstallSelectedModsAsync()
    {
        if (IsInstalling || SelectedCount == 0) return;
        using var cancellation = new CancellationTokenSource();
        using var operation = DownloadCenter.Begin($"Install {ContentTitle.ToLowerInvariant()}: {Path.GetFileName(_instancePath)}", cancellation.Cancel);
        IsInstalling = true;
        try { await InstallSelectionAsync(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { operation.Cancelled(); StatusMessage = "Installation cancelled. Verified downloads remain cached."; }
        catch (Exception ex) { operation.Fail(ex); StatusMessage = $"Installation failed: {ex.Message}"; }
        finally { IsInstalling = false; }
    }

    partial void OnStatusMessageChanged(string value) => DownloadCenter.Current?.Report(value);

    private async Task InstallSelectionAsync(CancellationToken token)
    {
        var selected = AllKnownItems().Where(mod => mod.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one item to install.";
            return;
        }

        if (_projectType == "mod")
        {
            await InstallModsWithDependenciesAsync(selected, token);
            return;
        }

        string targetDir = Path.Combine(_instancePath, _projectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            _ => "mods"
        });
        Directory.CreateDirectory(targetDir);

        int installed = 0;
        var transfer = new TransferProgress(snapshot => DownloadCenter.Current?.Report(snapshot));
        transfer.Begin(ContentTitle, selected.Count, 0);
        var failed = new List<string>();
        foreach (var item in selected)
        {
            token.ThrowIfCancellationRequested();
            StatusMessage = $"Installing {installed + 1}/{selected.Count}: {item.Item.Title}...";
            try
            {
                await EnsureVersionsAsync(item);
                var version = item.SelectedVersion;
                if (version == null || !IsCompatibleVersion(version))
                {
                    failed.Add($"{item.Item.Title}: no compatible version for Minecraft {_gameVersion} / {_loader}");
                    continue;
                }

                var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.First();
                string url = file.Url;

                string fileName = Path.GetFileName(string.IsNullOrWhiteSpace(file.Filename)
                    ? new Uri(url).AbsolutePath : file.Filename);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    failed.Add(item.Item.Title);
                    continue;
                }

                string destination = Path.Combine(targetDir, fileName);
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    file.Hashes.TryGetValue("sha1", out string? hash);
                    long received = 0;
                    await DownloadCache.EnsureAsync(url, temporary, token, hash, received: bytes => { received += bytes; transfer.Received(bytes); });
                    if (file.Hashes.Count > 0 && !await MatchesHashAsync(temporary, file)) throw new IOException("Downloaded file checksum is invalid.");
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, overwrite: true);
                    transfer.Complete(new FileInfo(destination).Length, received);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                item.IsSelected = false;
                installed++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed.Add($"{item.Item.Title}: {ex.Message}");
            }
        }

        OnPropertyChanged(nameof(SelectedCount));
        StatusMessage = failed.Count == 0
            ? $"Installed {installed} {ContentTitle.ToLowerInvariant()} successfully."
            : $"Installed {installed}; unavailable: {string.Join(", ", failed)}";
        if (failed.Count > 0) DownloadCenter.Current?.Fail(new IOException(StatusMessage));
    }

    private async Task InstallModsWithDependenciesAsync(IReadOnlyList<ModrinthModItemViewModel> selected, CancellationToken token)
    {
        StatusMessage = "Checking required dependencies and installed libraries...";
        var roots = new List<ModInstallEntry>();
        foreach (var item in selected)
        {
            token.ThrowIfCancellationRequested();
            await EnsureVersionsAsync(item).WaitAsync(token);
            if (item.SelectedVersion == null || !IsCompatibleVersion(item.SelectedVersion))
                throw new InvalidOperationException($"No compatible version selected for {item.Item.Title}.");
            roots.Add(new ModInstallEntry(item.Item.ProjectId, item.Item.Title, item.SelectedVersion));
        }
        string folder = Path.Combine(_instancePath, "mods");
        var plan = await new ModDependencyResolver(_modrinthService).ResolveAsync(roots, folder, _gameVersion, _loader).WaitAsync(token);
        var additional = plan.Where(entry => entry.IsDependency).ToArray();
        if (additional.Length > 0)
        {
            StatusMessage = $"Waiting for permission to install {additional.Length} required dependencies...";
            if (ConfirmDependenciesAsync == null || !await ConfirmDependenciesAsync(additional))
            {
                StatusMessage = "Installation cancelled. Required dependencies were not approved; no mods were installed.";
                DownloadCenter.Current?.Cancelled();
                return;
            }
        }

        // Stage the entire approved plan before adding anything to the instance.
        // A failed dependency download must not leave its parent mod installed.
        var staged = new List<(string Temporary, string Destination)>();
        var usedNames = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Directory.CreateDirectory(folder);
        try
        {
            int count = 0;
            var transfer = new TransferProgress(snapshot => DownloadCenter.Current?.Report(snapshot));
            transfer.Begin("Mods and approved dependencies", plan.Count, 0);
            foreach (var entry in plan)
            {
                token.ThrowIfCancellationRequested();
                var file = entry.Version.Files.FirstOrDefault(f => f.Primary) ?? entry.Version.Files.First();
                string name = string.IsNullOrWhiteSpace(file.Filename) ? Path.GetFileName(new Uri(file.Url).AbsolutePath) : file.Filename;
                if (Path.GetFileName(name) != name || name.Contains('\\') || name.Contains('/') || !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid mod filename for {entry.Title}.");
                if (!usedNames.Add(name)) throw new InvalidOperationException($"Two projects use the same filename: {name}.");
                string destination = Path.Combine(folder, name);
                if (File.Exists(destination + ".disabled")) throw new InvalidOperationException($"{entry.Title} is disabled. Enable it first.");
                if (File.Exists(destination))
                {
                    if (await MatchesHashAsync(destination, file)) { transfer.Complete(new FileInfo(destination).Length, 0); continue; }
                    throw new IOException($"{name} already exists and could not be verified as the requested version. No files were overwritten.");
                }
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                staged.Add((temporary, destination));
                StatusMessage = $"Downloading {++count}/{plan.Count}: {entry.Title} {entry.Version.DisplayName}...";
                file.Hashes.TryGetValue("sha1", out string? hash);
                long received = 0;
                await DownloadCache.EnsureAsync(file.Url, temporary, token, hash, received: bytes => { received += bytes; transfer.Received(bytes); });
                if (file.Hashes.Count > 0 && !await MatchesHashAsync(temporary, file))
                    throw new IOException($"Download verification failed for {entry.Title}.");
                transfer.Complete(new FileInfo(temporary).Length, received);
            }
            token.ThrowIfCancellationRequested();
            foreach (var file in staged)
                if (File.Exists(file.Destination)) throw new IOException("Files changed during installation. Try again.");
            foreach (var file in staged) File.Move(file.Temporary, file.Destination, overwrite: false);
            foreach (var item in selected) item.IsSelected = false;
            OnPropertyChanged(nameof(SelectedCount));
            StatusMessage = $"Installed {selected.Count} selected mod(s) with {additional.Length} required dependencies. Existing libraries were kept.";
        }
        finally
        {
            foreach (var file in staged) if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
        }
    }

    private static async Task<bool> MatchesHashAsync(string path, ModrinthFileItem file)
    {
        await using var stream = File.OpenRead(path);
        if (file.Hashes.TryGetValue("sha512", out var sha512))
            return Convert.ToHexString(await SHA512.HashDataAsync(stream)).Equals(sha512, StringComparison.OrdinalIgnoreCase);
        if (file.Hashes.TryGetValue("sha1", out var sha1))
            return Convert.ToHexString(await SHA1.HashDataAsync(stream)).Equals(sha1, StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
