using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Models;
using VortexLauncher.Core.Network;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class InstanceDetailsViewModel : ObservableObject, IDisposable
{
    private readonly InstanceModel _instance;
    private readonly Func<string, Task>? _joinServerCallback;
    public Func<string, Task<bool>>? ConfirmRemovalAsync { get; set; }
    public Func<Task>? RepairCallback { get; set; }
    public Func<Task>? ClientUpdateCallback { get; set; }
    public Func<Task>? ShareCallback { get; set; }
    public Func<ModPreflightReport, Task>? ShowModCheckAsync { get; set; }
    private CancellationTokenSource? _modCheckCancellation;
    public void CancelModCheck() => _modCheckCancellation?.Cancel();
    [RelayCommand] private async Task SharePackAsync() { if (CanMaintain && ShareCallback != null) await ShareCallback(); }
    public bool HasUndoRemoval => Core.Management.RemovalUndoHistory.HasUndo(_instance.InstancePath);
    public bool CanUndoRemoval => HasUndoRemoval && CanMaintain;
    private void OnUndoChanged(string root) { if (Core.Management.RemovalUndoHistory.Matches(_instance.InstancePath, root)) { OnPropertyChanged(nameof(HasUndoRemoval)); OnPropertyChanged(nameof(CanUndoRemoval)); RefreshItems(); } }
    [RelayCommand] private void UndoRemoval()
    {
        if (!CanUndoRemoval) return;
        try { int count = Core.Management.RemovalUndoHistory.UndoLast(_instance.InstancePath, () => _instance.CanChangeFiles && !IsImporting && !UpdateAllModsCommand.IsRunning && !RemoveItemCommand.IsRunning); StatusMessage = $"Restored {count} file(s)."; }
        catch (Exception ex) { StatusMessage = "Could not undo removal: " + ex.Message; }
        finally { RefreshItems(); }
    }
    public bool CanMaintain => _instance.CanChangeFiles && !IsImporting && !UpdateAllModsCommand.IsRunning && !RemoveItemCommand.IsRunning;
    public bool SupportsClientUpdate => VortexClientService.IsCompatible(_instance.Version, _instance.Loader);
    [RelayCommand] private async Task RepairInstanceAsync() { if (CanMaintain && RepairCallback != null) { await RepairCallback(); RefreshItems(); } }
    [RelayCommand] private async Task UpdateVortexClientAsync() { if (CanMaintain && ClientUpdateCallback != null) { await ClientUpdateCallback(); RefreshItems(); } }
    public bool CanRemoveFiles => IsInstallableSection && _instance.CanChangeFiles && !IsImporting && !UpdateAllModsCommand.IsRunning && !RemoveItemCommand.IsRunning;

    [ObservableProperty]
    private string _selectedSection = "Mods";

    public string InstanceTitle => _instance.Name;
    public string InstanceArtworkPath => _instance.CoverImagePath;
    public string InstanceSubtitle => $"{_instance.Version} • {_instance.Loader}";
    public bool IsInstallableSection => SelectedSection is "Mods" or "Resource packs" or "Shader packs";
    public bool IsModsSection => SelectedSection == "Mods";
    public bool IsServersSection => SelectedSection == "Servers";
    public bool IsScreenshotsSection => SelectedSection == "Screenshots";
    public bool IsFileListSection => !IsServersSection && !IsScreenshotsSection;
    public Thickness EmptyStateMargin => IsServersSection ? new Thickness(0, 95, 0, 0) : new Thickness(0);
    public bool CanImportFiles => IsInstallableSection && _instance.IsInstalled && _instance.CanChangeFiles && !IsImporting && !UpdateAllModsCommand.IsRunning && !RemoveItemCommand.IsRunning;
    [ObservableProperty] private bool _isImporting;
    partial void OnIsImportingChanged(bool value) { OnPropertyChanged(nameof(CanImportFiles)); OnPropertyChanged(nameof(CanRemoveFiles)); OnPropertyChanged(nameof(CanUndoRemoval)); OnPropertyChanged(nameof(CanMaintain)); }
    public bool IsEmpty => IsServersSection ? Servers.Count == 0 : Items.Count == 0;
    public string EmptyTitle => SelectedSection switch
    {
        "Mods" => "Your next adventure starts here",
        "Resource packs" => "Give your world a new look",
        "Shader packs" => "A little light changes everything",
        "Worlds" => "No worlds yet",
        "Screenshots" => "Your best moments belong here",
        "Logs" => "Nothing to debug yet",
        "Options" => "Game settings will appear here",
        _ => "Find your next multiplayer home"
    };
    public string EmptyDescription => SelectedSection switch
    {
        "Mods" => "Browse mods or drop .jar files here to add them to this instance.",
        "Resource packs" => "Browse resource packs or drop a resource pack .zip here.",
        "Shader packs" => "Browse shaders or drop a shader .zip here. A compatible shader mod is required in-game.",
        "Worlds" => "Create a world in Minecraft and it will show up here.",
        "Screenshots" => "Press F2 in Minecraft to capture a screenshot, then refresh this page.",
        "Logs" => "Launch this instance to generate game logs.",
        "Options" => "Launch Minecraft once to create options.txt.",
        _ => "Enter a server name and address above to save your first server."
    };

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Sections { get; } = new()
    {
        "Mods", "Resource packs", "Shader packs", "Worlds",
        "Screenshots", "Logs", "Options", "Servers"
    };

    public ObservableCollection<InstanceContentItem> Items { get; } = new();
    public ObservableCollection<ServerEntry> Servers { get; } = new();

    [ObservableProperty]
    private string _newServerName = string.Empty;

    [ObservableProperty]
    private string _newServerAddress = string.Empty;

    public InstanceDetailsViewModel(InstanceModel instance, Func<string, Task>? joinServerCallback = null)
    {
        _instance = instance;
        Core.Management.RemovalUndoHistory.Changed += OnUndoChanged;
        _joinServerCallback = joinServerCallback;
        _instance.PropertyChanged += OnContentStateChanged;
        UpdateAllModsCommand.PropertyChanged += OnContentStateChanged;
        RemoveItemCommand.PropertyChanged += OnContentStateChanged;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Servers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        RefreshItems();
    }

    private void OnContentStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanMaintain));
        OnPropertyChanged(nameof(CanUndoRemoval));
        OnPropertyChanged(nameof(CanRemoveFiles));
        OnPropertyChanged(nameof(CanImportFiles));
    }

    public void Dispose()
    {
        CancelModCheck();
        Core.Management.RemovalUndoHistory.Changed -= OnUndoChanged;
        ShareCallback = null;
        ShowModCheckAsync = null;
        ConfirmRemovalAsync = null;
        OpenModsManagerAsync = null;
        ExportReportAsync = null;
        _instance.PropertyChanged -= OnContentStateChanged;
        UpdateAllModsCommand.PropertyChanged -= OnContentStateChanged;
        RemoveItemCommand.PropertyChanged -= OnContentStateChanged;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsInstallableSection));
        OnPropertyChanged(nameof(CanRemoveFiles));
        OnPropertyChanged(nameof(IsModsSection));
        OnPropertyChanged(nameof(IsServersSection));
        OnPropertyChanged(nameof(IsScreenshotsSection));
        OnPropertyChanged(nameof(IsFileListSection));
        OnPropertyChanged(nameof(EmptyStateMargin));
        OnPropertyChanged(nameof(CanImportFiles));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        RefreshItems();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool AcceptsFile(string path) => IsInstallableSection &&
        string.Equals(Path.GetExtension(path), IsModsSection ? ".jar" : ".zip", StringComparison.OrdinalIgnoreCase);

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (!CanImportFiles)
        {
            StatusMessage = "Install this instance and stop the game before adding files.";
            return;
        }
        string section = SelectedSection;
        string folder = section switch { "Mods" => "mods", "Resource packs" => "resourcepacks", _ => "shaderpacks" };
        string destinationFolder = Path.Combine(_instance.InstancePath, folder);
        var files = paths.Distinct(StringComparer.Ordinal).ToArray();
        int added = 0;
        var skipped = new List<string>();
        IsImporting = true;
        try
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in files)
            {
                string name = Path.GetFileName(source);
                string expected = section == "Mods" ? ".jar" : ".zip";
                if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), expected, StringComparison.OrdinalIgnoreCase))
                { skipped.Add($"{name}: expected {expected}"); continue; }
                if (!_instance.CanChangeFiles) { skipped.Add($"{name}: instance is busy"); continue; }
                string destination = Path.Combine(destinationFolder, name);
                if (File.Exists(destination) || (section == "Mods" && File.Exists(destination + ".disabled")))
                { skipped.Add($"{name}: already exists"); continue; }
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output);
                    using (var archive = ZipFile.OpenRead(temporary))
                    {
                        bool valid = section switch
                        {
                            "Resource packs" => archive.GetEntry("pack.mcmeta") != null,
                            "Shader packs" => archive.Entries.Any(e => e.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)),
                            _ => archive.Entries.Count > 0
                        };
                        if (!valid) throw new InvalidDataException("archive does not contain the expected content");
                    }
                    if (!_instance.CanChangeFiles) throw new IOException("instance is busy");
                    File.Move(temporary, destination, overwrite: false);
                    added++;
                }
                catch (Exception ex) { skipped.Add($"{name}: {ex.Message}"); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            StatusMessage = $"Added {added} file(s) to {section}." +
                (skipped.Count > 0 ? $" Skipped: {string.Join("; ", skipped)}" : "");
        }
        catch (Exception ex) { StatusMessage = $"Import failed: {ex.Message}"; }
        finally { IsImporting = false; RefreshItems(); }
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshItems();
    }

    [RelayCommand]
    private async Task CheckCompatibilityAsync()
    {
        using var cancellation = new CancellationTokenSource(); _modCheckCancellation = cancellation;
        StatusMessage = "Checking local mod metadata...";
        try
        {
            var report = await new ModPreflightService().CheckAsync(_instance.InstancePath, _instance.Version, _instance.Loader, _instance.LoaderVersion, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = report.Counts;
            if (ShowModCheckAsync != null) await ShowModCheckAsync(report);
            else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                await new ModCheckWindow(report, _instance.InstancePath).ShowDialog(desktop.MainWindow);
        }
        catch (OperationCanceledException) { StatusMessage = "Mod check cancelled."; }
        catch (Exception ex) { StatusMessage = $"Compatibility check failed: {ex.Message}"; }
        finally { _modCheckCancellation = null; }
    }

    [RelayCommand]
    private void AddServer()
    {
        if (string.IsNullOrWhiteSpace(NewServerAddress)) return;
        Servers.Add(new ServerEntry
        {
            Name = string.IsNullOrWhiteSpace(NewServerName) ? "Minecraft Server" : NewServerName.Trim(),
            Address = NewServerAddress.Trim()
        });
        NewServerName = string.Empty;
        NewServerAddress = string.Empty;
        SaveServers();
    }

    [RelayCommand]
    private void RemoveServer(ServerEntry? server)
    {
        if (server == null) return;
        Servers.Remove(server);
        SaveServers();
    }

    [RelayCommand]
    private async Task CopyServerAddressAsync(ServerEntry? server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Address)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(server.Address);
            StatusMessage = $"Copied {server.Address}";
        }
    }

    [RelayCommand]
    private async Task JoinServerAsync(ServerEntry? server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Address)) return;
        if (_joinServerCallback == null)
        {
            StatusMessage = "Server launch is unavailable from this window.";
            return;
        }

        StatusMessage = $"Launching {server.Name}...";
        await _joinServerCallback(server.Address);
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section)) SelectedSection = section;
    }

    [RelayCommand]
    private async Task DownloadModsAsync()
    {
        if (!IsInstallableSection) return;

        string projectType = SelectedSection switch
        {
            "Resource packs" => "resourcepack",
            "Shader packs" => "shader",
            _ => "mod"
        };
        var downloadVm = new DownloadModsViewModel(
            _instance.Version, _instance.Loader, _instance.InstancePath, projectType);
        var window = new DownloadModsWindow { DataContext = downloadVm };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            await window.ShowDialog(desktop.MainWindow);
            RefreshItems();
        }
    }

    [RelayCommand]
    private void OpenSectionFolder()
    {
        string path = SelectedSection == "Options"
            ? Path.Combine(_instance.InstancePath, "options.txt")
            : Path.Combine(_instance.InstancePath, SelectedSection switch
            {
                "Mods" => "mods",
                "Resource packs" => "resourcepacks",
                "Shader packs" => "shaderpacks",
                "Worlds" => "saves",
                "Screenshots" => "screenshots",
                "Logs" => "logs",
                _ => string.Empty
            });

        try
        {
            if (SelectedSection == "Options")
            {
                if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                PlatformSupport.OpenPath(path);
                return;
            }

            Directory.CreateDirectory(path);
            PlatformSupport.OpenPath(path);
        }
        catch { }
    }

    [RelayCommand]
    private void OpenItem(InstanceContentItem? item)
    {
        if (item == null || !File.Exists(item.Path) && !Directory.Exists(item.Path)) return;

        try
        {
            PlatformSupport.OpenPath(item.Path);
        }
        catch { }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(InstanceContentItem? item)
    {
        if (item == null || !Items.Contains(item) || !IsInstallableSection || !_instance.CanChangeFiles || IsImporting || UpdateAllModsCommand.IsRunning) return;
        string section = SelectedSection;
        if (ConfirmRemovalAsync == null || !await ConfirmRemovalAsync(Path.GetFileName(item.Path))) return;
        if (section != SelectedSection || !_instance.CanChangeFiles || IsImporting || UpdateAllModsCommand.IsRunning || !Items.Contains(item)) return;
        try
        {
            string backup = Core.Management.InstanceContentRemoval.MoveToBackup(_instance.InstancePath, section, item.Path);
            Core.Management.RemovalUndoHistory.Remember(_instance.InstancePath, new[] { new Core.Management.RemovedContent(section, backup) });
            RefreshItems();
            StatusMessage = "File removed. Use Undo removal to restore it.";
        }
        catch (Exception ex) { StatusMessage = $"Could not remove file: {ex.Message}"; }
    }

    [RelayCommand]
    private void ToggleMod(InstanceContentItem? item)
    {
        if (item == null || !_instance.CanChangeFiles || IsImporting || RemoveItemCommand.IsRunning || !string.Equals(SelectedSection, "Mods", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            // Use the actual file name instead of IsEnabled because a ToggleSwitch
            // updates its two-way binding before the command is invoked.
            bool isDisabled = item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            Core.Management.ModFiles.SetEnabled(_instance.InstancePath, item.Path, isDisabled);
            RefreshItems();
        }
        catch { }
    }

    public Func<bool, Task>? OpenModsManagerAsync { get; set; }
    public Func<Task<string>>? ExportReportAsync { get; set; }

    [RelayCommand]
    private async Task ManageModsAsync()
    {
        if (!_instance.CanChangeFiles || IsImporting || RemoveItemCommand.IsRunning || UpdateAllModsCommand.IsRunning || OpenModsManagerAsync == null) return;
        await OpenModsManagerAsync(false);
        RefreshItems();
    }

    [RelayCommand]
    private async Task UpdateAllModsAsync()
    {
        if (!_instance.CanChangeFiles || IsImporting || RemoveItemCommand.IsRunning || OpenModsManagerAsync == null) return;
        await OpenModsManagerAsync(true);
        RefreshItems();
    }

    [RelayCommand]
    private async Task SaveErrorReportAsync()
    {
        if (ExportReportAsync == null) return;
        try { StatusMessage = await ExportReportAsync(); }
        catch (Exception ex) { StatusMessage = "Could not save report: " + ex.Message; }
    }

    private void RefreshItems()
    {
        Items.Clear();
        if (SelectedSection == "Servers")
        {
            LoadServers();
            return;
        }
        string root = _instance.InstancePath;
        string folder = SelectedSection switch
        {
            "Mods" => "mods",
            "Resource packs" => "resourcepacks",
            "Shader packs" => "shaderpacks",
            "Worlds" => "saves",
            "Screenshots" => "screenshots",
            "Logs" => "logs",
            _ => string.Empty
        };

        if (SelectedSection == "Options")
        {
            string options = Path.Combine(root, "options.txt");
            if (!File.Exists(options)) return;
            Items.Add(new InstanceContentItem(
                File.Exists(options) ? "options.txt" : "options.txt (not created yet)",
                "Game settings file", string.Empty, options, File.Exists(options), false));
            return;
        }

        string directory = Path.Combine(root, folder);
        if (!Directory.Exists(directory)) return;

        bool directories = SelectedSection == "Worlds";
        var paths = directories
            ? Directory.GetDirectories(directory)
            : Directory.GetFiles(directory);

        foreach (string path in paths.OrderBy(Path.GetFileName))
        {
            string name = Path.GetFileName(path);
            bool enabled = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            string version = string.Empty;
            if (SelectedSection == "Mods")
            {
                string fileName = Path.GetFileNameWithoutExtension(enabled ? name : name[..^9]);
                int separator = fileName.IndexOf('-');
                if (separator > 0)
                {
                    name = fileName[..separator].Replace('_', ' ');
                    version = fileName[(separator + 1)..];
                }
                else
                {
                    name = fileName.Replace('_', ' ');
                }
            }

            string subtitle = directories
                ? "Minecraft world"
                : $"{new FileInfo(path).Length / 1024.0:0.#} KB";
            Items.Add(new InstanceContentItem(name, subtitle, version, path, enabled,
                SelectedSection == "Mods"));
        }
    }

    private string ServersFilePath => Path.Combine(_instance.InstancePath, "servers.json");

    private void LoadServers()
    {
        Servers.Clear();
        if (!File.Exists(ServersFilePath)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<ServerEntry>>(File.ReadAllText(ServersFilePath));
            if (loaded != null)
                foreach (var server in loaded) Servers.Add(server);
        }
        catch { }
    }

    private void SaveServers()
    {
        try
        {
            Directory.CreateDirectory(_instance.InstancePath);
            File.WriteAllText(ServersFilePath, JsonSerializer.Serialize(Servers,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public partial class InstanceContentItem : ObservableObject
{
    public InstanceContentItem(string name, string subtitle, string version, string path, bool isEnabled, bool isMod)
    {
        Name = name;
        Subtitle = subtitle;
        Version = version;
        Path = path;
        IsEnabled = isEnabled;
        IsMod = isMod;
    }

    public string Name { get; }
    public string Subtitle { get; }
    public string Version { get; }
    public string Path { get; }
    public bool IsMod { get; }

    public IBrush NameBrush => IsEnabled ? Brushes.WhiteSmoke : Brushes.SlateGray;
    public IBrush SubtitleBrush => IsEnabled ? Brushes.SlateGray : Brushes.DimGray;
    public IBrush VersionBrush => IsEnabled ? Brushes.LightGray : Brushes.DimGray;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(SubtitleBrush));
        OnPropertyChanged(nameof(VersionBrush));
    }
}
