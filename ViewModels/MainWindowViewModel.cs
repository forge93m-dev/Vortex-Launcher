using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;
using VortexLauncher.Core.Theme;
using VortexLauncher.Models;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string InstallationMarkerFileName = ".vortex-installation-complete";

    private readonly GameLauncher _launcher = new();
    private readonly GameDownloader _downloader = new();
    private readonly InstanceManager _instanceManager;
    private readonly SettingsManager _settingsManager;
    private readonly AccountManager _accountManager;
    private LauncherSettings _currentSettings = new();
    private readonly bool _checkUpdatesOnStartup;
    private readonly UpdateService _updateService = new();
    private CancellationTokenSource? _installationCancellation;
    private Avalonia.Controls.WindowState _previousWindowState;
    private int _runningGames;
    private bool _minimizedForGame;

    [ObservableProperty]
    private bool _canCancelInstallation;

    [ObservableProperty]
    private string _transferDetails = string.Empty;

    [ObservableProperty]
    private bool _minimizeOnGameLaunch = true;

    [RelayCommand]
    private void CancelInstallation()
    {
        _installationCancellation?.Cancel();
        CanCancelInstallation = false;
        StatusMessage = "Stopping installation. Downloaded files will be kept...";
    }

    [RelayCommand]
    private void ToggleFavorite(InstanceModel? instance)
    {
        if (IsInitializing || instance == null || instance.MetadataProblem.Length > 0) return;
        instance.IsFavorite = !instance.IsFavorite;
        SaveInstanceMetadata(instance);
        SortInstances();
    }

    private void SortInstances()
    {
        var ordered = Instances.OrderByDescending(item => item.IsFavorite).ToArray();
        for (int index = 0; index < ordered.Length; index++)
            Instances.Move(Instances.IndexOf(ordered[index]), index);
    }

    public void RenameInstance(InstanceModel instance, string name)
    {
        if (IsInitializing || instance.MetadataProblem.Length > 0 || string.IsNullOrWhiteSpace(name)) return;
        instance.Name = name.Trim();
        SaveInstanceMetadata(instance);
    }

    [RelayCommand]
    private async Task CloneInstanceAsync(InstanceModel? instance)
    {
        if (IsInitializing || instance == null || !instance.CanChangeFiles || IsGlobalLoading || IsDownloading) return;
        IsGlobalLoading = true;
        LoadingTitle = $"Copying {instance.Name}";
        TransferDetails = "Copying worlds, mods and settings to an independent instance...";
        using var cancellation = new CancellationTokenSource();
        using var operation = DownloadCenter.Begin("Copy: " + instance.Name, cancellation.Cancel);
        _installationCancellation = cancellation; CanCancelInstallation = true;
        try
        {
            var transfer = new TransferProgress(s => { operation.Report(s); UpdateTransferProgress(s); });
            var clone = await Task.Run(() => _instanceManager.CloneAsync(instance, cancellationToken: cancellation.Token, transfer: transfer));
            Instances.Add(clone);
            SortInstances();
            SelectedInstance = clone;
            StatusMessage = $"Created {clone.Name}";
            operation.Complete();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { operation.Cancelled(); StatusMessage = "Copy stopped safely. Resume it from the recovery banner."; }
        catch (Exception ex) { operation.Fail(ex); StatusMessage = $"Copy failed: {ex.Message}"; }
        finally { CanCancelInstallation = false; await RefreshRecoveriesAsync(); TransferDetails = string.Empty; _installationCancellation = null; IsGlobalLoading = false; }
    }

    [ObservableProperty]
    private InstanceModel? _selectedInstance;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private bool _isInstallingUpdate;

    private LauncherUpdate? _availableUpdate;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isGlobalLoading;

    [ObservableProperty]
    private string _loadingTitle = "Loading...";

    [ObservableProperty]
    private string _activeTab = "Instances";

    public bool IsInstancesTab => ActiveTab == "Instances";
    public bool IsModpacksTab => ActiveTab == "Modpacks";
    public bool IsAccountsTab => ActiveTab == "Accounts";
    public bool IsSettingsTab => ActiveTab == "Settings";
    public bool HasSelectedInstance => SelectedInstance != null;

    public string PageEyebrow => ActiveTab switch
    {
        "Modpacks" => "DISCOVER", "Accounts" => "YOUR IDENTITY", "Settings" => "MAKE IT YOURS", _ => "YOUR COLLECTION"
    };
    public string PageTitle => ActiveTab switch
    {
        "Modpacks" => "Discover modpacks", "Accounts" => "Player profiles", "Settings" => "Launcher settings", _ => "Your library"
    };
    public string PageDescription => ActiveTab switch
    {
        "Modpacks" => "Find a new world to get lost in.",
        "Accounts" => "Choose the player profile for your next session.",
        "Settings" => "A launcher that feels right for you.",
        _ => "All your worlds, ready when you are."
    };

    [ObservableProperty]
    private int _ramGb = 4;

    [ObservableProperty]
    private string _customJavaPath = string.Empty;

    [ObservableProperty]
    private string _jvmArgs = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";

    [ObservableProperty]
    private string _accentColor = "#7AA2F7";

    [ObservableProperty]
    private string _themeName = "Tokyo Night";

    [ObservableProperty]
    private bool _isStartupGreetingVisible = true;

    public string StartupUsername { get; } = Environment.UserName;

    [ObservableProperty]
    private string _startupGreeting = "Welcome";

    [ObservableProperty]
    private AccountModel _activeAccount = new("Steve");

    [ObservableProperty]
    private string _newAccountUsername = string.Empty;

    public ObservableCollection<AccountModel> Accounts { get; } = new();
    public ObservableCollection<InstanceModel> Instances { get; } = new();
    public ObservableCollection<ThemePresetViewModel> ThemePresets { get; } = new();

    public ModpacksViewModel ModpacksVm { get; }
    public InstanceLibraryViewModel Library { get; }

    public MainWindowViewModel() : this(new InstanceManager(), new SettingsManager(), new AccountManager()) { }
    internal MainWindowViewModel(InstanceManager instances, SettingsManager settings, AccountManager accounts, bool checkUpdates = true)
    {
        _checkUpdatesOnStartup = checkUpdates;
        _instanceManager = instances; _settingsManager = settings; _accountManager = accounts;
        Library = new InstanceLibraryViewModel(Instances);
        Accounts.Add(ActiveAccount);

        ModpacksVm = new ModpacksViewModel(
            ImportInstanceArchive,
            (isLoading, title, status) =>
            {
                void ApplyLoadingState()
                {
                    IsGlobalLoading = isLoading;
                    LoadingTitle = title;
                    StatusMessage = status;
                }
                if (Dispatcher.UIThread.CheckAccess()) ApplyLoadingState();
                else Dispatcher.UIThread.Post(ApplyLoadingState);
            });

        ThemePresets.Add(new ThemePresetViewModel("Soft Slate", "#8BA8D8", "#181C24", "#1E232D", "#252B36", "#394353", "#E2E6EF", "#A4AFC2"));
        ThemePresets.Add(new ThemePresetViewModel("Tokyo Night", "#7AA2F7", "#131722", "#191E2B", "#202636", "#344059", "#DFE6F3", "#A0ADC4"));
        ThemePresets.Add(new ThemePresetViewModel("Catppuccin", "#F5C2E7", "#11111B", "#181825", "#1E1E2E", "#313244", "#CDD6F4", "#A6ADC8"));
        ThemePresets.Add(new ThemePresetViewModel("Nord", "#88C0D0", "#2E3440", "#3B4252", "#434C5E", "#4C566A", "#ECEFF4", "#D8DEE9"));
        ThemePresets.Add(new ThemePresetViewModel("Dracula", "#BD93F9", "#191A21", "#21222C", "#282A36", "#44475A", "#F8F8F2", "#BFBFBF"));
        ThemePresets.Add(new ThemePresetViewModel("Cyber Neon", "#FF007F", "#090A12", "#111225", "#171A32", "#30365C", "#FFFFFF", "#9CA8D7"));
        ThemePresets.Add(new ThemePresetViewModel("Emerald", "#50FA7B", "#07110D", "#0C1B14", "#10251A", "#1B4930", "#F0FFF4", "#91B7A0"));

        Library.IsLoading = true;
    }

    private Task CheckForUpdatesAsync() => CheckLauncherUpdatesAsync();

    partial void OnAccentColorChanged(string value)
    {
        ThemeManager.SetAccentColor(value);
    }

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(PageEyebrow));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(IsInstancesTab));
        OnPropertyChanged(nameof(IsModpacksTab));
        OnPropertyChanged(nameof(IsAccountsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
    }

    partial void OnSelectedInstanceChanged(InstanceModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedInstance));
    }

    private bool _isImportingArchive;

    public async Task ImportInstanceArchive(string archivePath)
    {
        if (IsInitializing) return;
        if (_isImportingArchive || IsGlobalLoading || IsDownloading) return;
        _isImportingArchive = true;
        DownloadOperation? operation = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var preview = await Task.Run(() => ArchivePreview.Inspect(archivePath));
            if (!await TaskDialog.ConfirmAsync("Import preview", preview.Summary, "Import instance")) return;
            operation = DownloadCenter.Begin($"Import: {preview.Name}", cancellation.Cancel);
            StatusMessage = "Importing instance...";
            IsGlobalLoading = true;
            LoadingTitle = $"Importing {preview.Name}";
            _installationCancellation = cancellation;
            CanCancelInstallation = true;
            var transfer = new TransferProgress(snapshot => { operation.Report(snapshot); UpdateTransferProgress(snapshot); });
            var imported = await Task.Run(() => _instanceManager.ImportArchiveAsync(archivePath, preview.Fingerprint, cancellation.Token, transfer));
            imported.IsInstalled = IsInstanceReady(imported);
            Instances.Add(imported);
            SortInstances();
            SelectedInstance = imported;
            StatusMessage = $"Imported {imported.Name}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            operation?.Cancelled();
            StatusMessage = "Import cancelled. The incomplete instance remains hidden; existing instances were not changed.";
        }
        catch (Exception ex)
        {
            operation?.Fail(ex);
            StatusMessage = $"Import failed: {ex.Message}";
            TaskDialog.ShowIssue(new LaunchIssue("Import failed", ex.Message, "Check the archive format and metadata. No existing instance was overwritten."), ex.ToString());
        }
        finally
        {
            operation?.Dispose();
            if (operation != null) CanCancelInstallation = false;
            _isImportingArchive = false;
            if (operation != null) await RefreshRecoveriesAsync();
            if (operation != null) { IsGlobalLoading = false; _installationCancellation = null; TransferDetails = ""; }
        }
    }

    public void ExportSelectedInstance(string archivePath)
    {
        ExportInstance(SelectedInstance, archivePath);
    }

    public void ExportInstance(InstanceModel? instance, string archivePath)
    {
        if (instance == null) return;
        try
        {
            _instanceManager.ExportArchive(instance, archivePath);
            StatusMessage = $"Exported {instance.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void ApplySettings(LauncherSettings settings)
    {
        _currentSettings = settings;
        StartupGreeting = settings.HasLaunchedBefore ? "Welcome Back" : "Welcome";
        settings.HasLaunchedBefore = true;
        RamGb = settings.RamAllocationGb;
        MinimizeOnGameLaunch = settings.MinimizeOnGameLaunch;
        CustomJavaPath = settings.CustomJavaPath;
        JvmArgs = settings.JvmArguments;
        ThemeName = settings.ThemeName;
        AccentColor = string.IsNullOrWhiteSpace(settings.AccentColor) ? "#7AA2F7" : settings.AccentColor;
        var preset = ThemePresets.FirstOrDefault(x => x.Name == ThemeName) ?? ThemePresets[0];
        ThemeManager.SetTheme(preset.Name, AccentColor, preset.Background, preset.Sidebar,
            preset.Panel, preset.Border, preset.Text, preset.Subtext);
    }

    [RelayCommand]
    public void ApplyPreset(ThemePresetViewModel preset)
    {
        if (preset != null)
        {
            ThemeName = preset.Name;
            AccentColor = preset.HexColor;
            ThemeManager.SetTheme(preset.Name, preset.HexColor, preset.Background, preset.Sidebar,
                preset.Panel, preset.Border, preset.Text, preset.Subtext);
        }
    }

    [RelayCommand]
    private void SelectTab(string tabName)
    {
        ActiveTab = tabName;
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (IsInitializing || string.IsNullOrWhiteSpace(NewAccountUsername)) return;

        var acc = new AccountModel(NewAccountUsername);
        Accounts.Add(acc);
        ActiveAccount = acc;
        NewAccountUsername = string.Empty;
        PersistAccounts();
    }

    [RelayCommand]
    private void SelectAccount(AccountModel? account)
    {
        if (!IsInitializing && account != null)
        {
            ActiveAccount = account;
            PersistAccounts();
        }
    }

    [RelayCommand]
    private void DeleteAccount(AccountModel? account)
    {
        if (IsInitializing || account == null) return;
        Accounts.Remove(account);

        if (ActiveAccount == account)
        {
            ActiveAccount = Accounts.FirstOrDefault() ?? new AccountModel("Steve");
        }
        PersistAccounts();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (IsInitializing) return;
        var settings = _currentSettings;
        settings.RamAllocationGb = RamGb;
        settings.CustomJavaPath = CustomJavaPath;
        settings.JvmArguments = JvmArgs;
        settings.AccentColor = AccentColor;
        settings.ThemeName = ThemeName;
        settings.MinimizeOnGameLaunch = MinimizeOnGameLaunch;
        try { await Task.Run(() => _settingsManager.SaveSettings(settings)); ThemeManager.SetAccentColor(AccentColor); StatusMessage = "Settings saved!"; }
        catch (Exception ex) { StatusMessage = "Settings were not saved: " + ex.Message; }
    }

    public async Task AddInstanceAsync(string name, string version, string loader, bool vortexClientEnabled = false)
    {
        if (IsInitializing) return;
        if (vortexClientEnabled && !VortexClientService.IsCompatible(version, loader))
        {
            StatusMessage = "Vortex Client is available for Minecraft 26.3 with Fabric only.";
            return;
        }
        var newInst = _instanceManager.CreateInstance(name, version, loader);
        newInst.VortexClientEnabled = vortexClientEnabled;
        _instanceManager.SaveInstance(newInst);
        Instances.Add(newInst);
        SortInstances();
        SelectedInstance = newInst;

        await InstallInstanceAsync(newInst);
    }

    public void AddInstance(string name, string version, string loader)
    {
        _ = AddInstanceAsync(name, version, loader);
    }

    public void SaveInstanceMetadata(InstanceModel instance)
    {
        try { _instanceManager.SaveInstance(instance); }
        catch (Exception ex) { StatusMessage = "Instance metadata was not saved: " + ex.Message; }
    }

    private static bool IsInstanceReady(InstanceModel instance)
    {
        if (instance.MetadataProblem.Length > 0 || instance.HasInterruptedOperation) return false;
        string markerPath = Path.Combine(instance.InstancePath, InstallationMarkerFileName);
        if (File.Exists(Path.Combine(instance.InstancePath, ".vortex-installation-pending"))) return false;
        if (File.Exists(markerPath)) return true;

        // Старые сборки, созданные до появления маркера, считаем готовыми,
        // если клиент Minecraft уже был скачан.
        return File.Exists(Path.Combine(instance.InstancePath, $"{instance.Version}.jar"));
    }

    private async Task InstallInstanceAsync(InstanceModel instance, bool repair = false)
    {
        if (IsInitializing || !instance.CanChangeFiles || (!repair && instance.IsInstalled) || IsDownloading || IsGlobalLoading) return;

        using var cancellation = new CancellationTokenSource();
        using var operation = DownloadCenter.Begin($"{(repair ? "Repair" : "Install")}: {instance.Name}", cancellation.Cancel);
        _installationCancellation = cancellation;
        CanCancelInstallation = true;
        TransferDetails = "Preparing download...";
        var transfer = new TransferProgress(UpdateTransferProgress);

        instance.IsInstalling = true;
        IsGlobalLoading = true;
        LoadingTitle = $"INSTALLING {instance.Name.ToUpperInvariant()}";
        StatusMessage = $"Preparing {instance.Version}...";

        try
        {
            await Task.Run(async () =>
            {
                if (repair) { InstanceRepair.ValidateTarget(instance); DownloadCache.ForgetVerification(instance.InstancePath); }
                OperationJournal.Begin(instance.InstancePath, repair ? "Repair" : "Install");
                await File.WriteAllTextAsync(Path.Combine(instance.InstancePath, ".vortex-installation-pending"), "Installing", cancellation.Token);
                string instanceFolderName = Path.GetFileName(instance.InstancePath);
                var minecraftService = new MinecraftService();
                string? versionJsonUrl = await minecraftService.GetVersionJsonUrlAsync(instance.Version).WaitAsync(cancellation.Token);
                if (string.IsNullOrWhiteSpace(versionJsonUrl))
                {
                    throw new InvalidOperationException($"Minecraft {instance.Version} was not found.");
                }

                await _downloader.DownloadGameAsync(
                    instanceFolderName,
                    instance.Version,
                    versionJsonUrl,
                    UpdateStatus, cancellation.Token, transfer);

                var gameArchitecture = PlatformSupport.GetGameArchitecture(instance.InstancePath, instance.Version);
                string javaPath = string.IsNullOrWhiteSpace(CustomJavaPath) ||
                                  CustomJavaPath.Contains("latest", StringComparison.OrdinalIgnoreCase) ||
                                  !File.Exists(CustomJavaPath)
                    ? JavaResolver.GetJavaForVersion(instance.Version, gameArchitecture)
                    : CustomJavaPath;
                PlatformSupport.ValidateJavaArchitecture(javaPath, gameArchitecture);

                if (instance.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Installing Fabric libraries...");
                    var fabricService = new FabricService();
                    FabricProfile? fabricProfile = await fabricService.GetFabricProfileAsync(instance.Version, instance.LoaderVersion).WaitAsync(cancellation.Token);
                    if (fabricProfile == null)
                    {
                        throw new InvalidOperationException($"Fabric is not available for Minecraft {instance.Version}.");
                    }

                    await _downloader.DownloadLoaderLibrariesAsync(
                        fabricProfile,
                        instance.InstancePath,
                        UpdateStatus, "https://maven.fabricmc.net/", cancellation.Token, transfer, repair);
                    instance.LoaderVersion = fabricProfile.Libraries?.FirstOrDefault(l => l.Name?.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal) == true)?.Name?.Split(':').Last() ?? instance.LoaderVersion;
                }
                else if (instance.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Installing Forge...");
                    if (!File.Exists(javaPath))
                        javaPath = await new JavaRuntimeManager().EnsureJavaAsync(
                            JavaResolver.GetRequiredJavaVersion(instance.Version), UpdateStatus, cancellation.Token, gameArchitecture);
                    var forgeService = new ForgeService();
                    string? forgeVersion = string.IsNullOrWhiteSpace(instance.LoaderVersion)
                        ? await forgeService.GetPromotedForgeVersionAsync(instance.Version).WaitAsync(cancellation.Token)
                        : (instance.LoaderVersion.StartsWith(instance.Version + "-", StringComparison.Ordinal)
                            ? instance.LoaderVersion : instance.Version + "-" + instance.LoaderVersion);
                    if (string.IsNullOrWhiteSpace(forgeVersion))
                    {
                        throw new InvalidOperationException(
                            $"Forge is not available for Minecraft {instance.Version}.");
                    }

                    bool installed = await forgeService.InstallForgeProfileAsync(
                        forgeVersion,
                        instance.Version,
                        instance.InstancePath,
                        javaPath,
                        UpdateStatus, cancellation.Token, transfer, repair);
                    if (!installed)
                    {
                        throw new InvalidOperationException($"Failed to install Forge {forgeVersion}.");
                    }
                    instance.LoaderVersion = forgeVersion;
                }
                else if (instance.Loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Installing Quilt libraries...");
                    var quiltService = new QuiltService();
                    FabricProfile? quiltProfile = await quiltService.GetQuiltProfileAsync(instance.Version, instance.LoaderVersion).WaitAsync(cancellation.Token);
                    if (quiltProfile == null)
                    {
                        throw new InvalidOperationException($"Quilt is not available for Minecraft {instance.Version}.");
                    }

                    await _downloader.DownloadLoaderLibrariesAsync(
                        quiltProfile,
                        instance.InstancePath,
                        UpdateStatus,
                        "https://maven.quiltmc.org/repository/release/", cancellation.Token, transfer, repair);
                    instance.LoaderVersion = quiltProfile.Libraries?.FirstOrDefault(l => l.Name?.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal) == true)?.Name?.Split(':').Last() ?? instance.LoaderVersion;
                }
                else if (!instance.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The {instance.Loader} loader is not supported by this launcher yet.");
                }

                if (instance.VortexClientEnabled && !repair)
                {
                    UpdateStatus("Installing Vortex Client...");
                    await new VortexClientService().InstallAsync(instance, cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(
                    Path.Combine(instance.InstancePath, InstallationMarkerFileName),
                    DateTimeOffset.UtcNow.ToString("O"), cancellation.Token);
                _instanceManager.SaveInstance(instance);
                OperationJournal.Complete(instance.InstancePath);
            });

            instance.IsInstalled = true;
            SaveInstanceMetadata(instance);
            StatusMessage = repair ? $"Repaired {instance.Name}. Worlds, mods and settings were preserved." : $"{instance.Name} is ready. You can install mods.";
            operation.Complete(StatusMessage);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            operation.Cancelled();
            instance.IsInstalled = IsInstanceReady(instance);
            StatusMessage = "Installation cancelled. Press Resume to continue with saved files.";
        }
        catch (Exception ex)
        {
            operation.Fail(ex);
            instance.IsInstalled = IsInstanceReady(instance);
            StatusMessage = $"Installation failed: {ex.Message}";
            TaskDialog.ShowIssue(CrashAnalyzer.Diagnose(new[] { ex.ToString() }), ex.ToString(), instance.InstancePath);
        }
        finally
        {
            instance.IsInstalling = false;
            CanCancelInstallation = false;
            await RefreshRecoveriesAsync();
            _installationCancellation = null;
            IsGlobalLoading = false;
        }
    }

    [RelayCommand]
    private void SelectInstance(InstanceModel? instance)
    {
        if (instance != null)
        {
            SelectedInstance = instance;
        }
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync(InstanceModel? instance)
    {
        var target = instance ?? SelectedInstance;
        if (target == null || !target.CanChangeFiles) return;

        try
        {
            target.IsBeingDeleted = true;
            StatusMessage = $"Removing '{target.Name}'...";
            await Task.Delay(280);

            if (Directory.Exists(target.InstancePath))
            {
                Directory.Delete(target.InstancePath, true);
            }

            Instances.Remove(target);
            if (SelectedInstance == target)
            {
                SelectedInstance = Instances.FirstOrDefault();
            }

            StatusMessage = $"Deleted instance '{target.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete instance: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenModsAsync(InstanceModel? instance)
    {
        var target = instance ?? SelectedInstance;
        if (target == null) return;

        if (target.IsInstalling)
        {
            StatusMessage = $"{target.Name} is still installing. Please wait.";
            return;
        }

        if (!target.IsInstalled)
        {
            StatusMessage = $"Install {target.Name} before adding mods.";
            return;
        }

        var modsVm = new InstanceModsViewModel(target.Version, target.Loader, target.InstancePath)
        {
            CanChangeFiles = () => target.CanChangeFiles
        };
        var window = new InstanceModsWindow
        {
            DataContext = modsVm
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await window.ShowDialog(desktop.MainWindow);
        }
    }

    [RelayCommand]
    private Task LaunchInstanceAsync(InstanceModel? instance)
        => LaunchInstanceCoreAsync(instance, null);

    private async Task LaunchInstanceCoreAsync(InstanceModel? instance, string? serverAddress)
    {
        var target = instance ?? SelectedInstance;
        if (target == null)
        {
            StatusMessage = "No instance selected!";
            return;
        }

        if (IsInitializing || IsDownloading || IsGlobalLoading || !target.CanChangeFiles) return;

        using var cancellation = new CancellationTokenSource();
        _installationCancellation = cancellation;
        CanCancelInstallation = true;
        TransferDetails = "Preparing game...";
        using var operation = DownloadCenter.Begin($"Launch preparation: {target.Name}", cancellation.Cancel);
        var transfer = new TransferProgress(UpdateTransferProgress);

        IsDownloading = true;
        StatusMessage = $"Preparing {target.Name}...";

        DebugLogWindow? logWindow = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            logWindow = new DebugLogWindow();
            logWindow.Show();
        });

        await Task.Run(async () =>
        {
            try
            {
                string instanceFolderName = Path.GetFileName(target.InstancePath);
                UpdateStatus("Checking mod compatibility...");
                var compatibility = await new ModCompatibilityService().CheckAsync(
                    target.InstancePath, target.Version, target.Loader, cancellation.Token);
                if (compatibility.Warnings.Count > 0)
                {
                    var confirmation = await Dispatcher.UIThread.InvokeAsync(() => ConfirmCompatibilityAsync(compatibility.Summary));
                    if (!confirmation)
                    {
                        UpdateStatus("Launch cancelled. Review the mod compatibility warnings in Manage.");
                        operation.Cancelled();
                        return;
                    }
                }
                cancellation.Token.ThrowIfCancellationRequested();

                var minecraftService = new MinecraftService();
                string? versionJsonUrl = await minecraftService.GetVersionJsonUrlAsync(target.Version).WaitAsync(cancellation.Token);
                if (string.IsNullOrEmpty(versionJsonUrl))
                {
                    throw new InvalidOperationException($"Failed to resolve manifest URL for Minecraft {target.Version}.");
                }

                await _downloader.DownloadGameAsync(
                    instanceFolderName,
                    target.Version,
                    versionJsonUrl,
                    (status, progress) =>
                    {
                        UpdateStatus(status, progress);
                    }, cancellation.Token, transfer);

                FabricProfile? fabricProfile = null;
                if (target.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Checking Fabric loader...");
                    var fabricService = new FabricService();
                    fabricProfile = await fabricService.GetFabricProfileAsync(target.Version, target.LoaderVersion).WaitAsync(cancellation.Token);

                    if (fabricProfile != null)
                    {
                        await _downloader.DownloadLoaderLibrariesAsync(fabricProfile, target.InstancePath, (status, progress) =>
                        {
                            UpdateStatus(status, progress);
                        }, "https://maven.fabricmc.net/", cancellation.Token, transfer);
                    }
                }
                else if (target.Loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Checking Quilt loader...");
                    var quiltService = new QuiltService();
                    fabricProfile = await quiltService.GetQuiltProfileAsync(target.Version, target.LoaderVersion).WaitAsync(cancellation.Token);

                    if (fabricProfile != null)
                    {
                        await _downloader.DownloadFabricLibrariesAsync(fabricProfile, target.InstancePath, (status, progress) =>
                        {
                            UpdateStatus(status, progress);
                        }, cancellation.Token, transfer);
                    }
                }
                else if (!target.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) &&
                         !target.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The {target.Loader} loader is not supported by this launcher yet.");
                }

                UpdateStatus("Launching Minecraft...");

                var process = await _launcher.LaunchAsync(
                    instanceFolderName,
                    target.Version,
                    ActiveAccount.Username,
                    ActiveAccount.Uuid,
                    CustomJavaPath,
                    RamGb,
                    JvmArgs,
                    target.Loader,
                    fabricProfile,
                    line => logWindow?.AppendLog(line),
                    (status, progress) => UpdateStatus(status, progress),
                    serverAddress, cancellation.Token, transfer, target.LoaderVersion
                );

                UpdateStatus("Game running!");
                operation.Complete("Minecraft started. Launch diagnostics continue in the game log.");
                DateTime startedAt = DateTime.UtcNow;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    target.IsRunning = true;
                    _runningGames++;
                    if (MinimizeOnGameLaunch && _runningGames == 1 &&
                        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow is { } window)
                    {
                        _previousWindowState = window.WindowState;
                        _minimizedForGame = true;
                        window.WindowState = Avalonia.Controls.WindowState.Minimized;
                        if (logWindow != null) logWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                    }
                });

                _ = Task.Run(async () =>
                {
                    await process.WaitForExitAsync();
                    int exitCode = process.ExitCode;
                    Dispatcher.UIThread.Post(() =>
                    {
                        target.IsRunning = false;
                        target.TotalPlayTimeSeconds += (long)Math.Max(0, (DateTime.UtcNow - startedAt).TotalSeconds);
                        _runningGames--;
                        if (_runningGames == 0 && _minimizedForGame && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                            desktop.MainWindow is { WindowState: Avalonia.Controls.WindowState.Minimized } window)
                        {
                            window.WindowState = _previousWindowState == Avalonia.Controls.WindowState.Minimized
                                ? Avalonia.Controls.WindowState.Normal : _previousWindowState;
                            window.Activate();
                        }
                        if (_runningGames == 0) _minimizedForGame = false;
                        try { _instanceManager.SaveInstance(target); }
                        catch (Exception ex) { logWindow?.AppendLog($"[WARN] Could not save playtime: {ex.Message}"); }
                        if (exitCode != 0)
                        {
                        if (logWindow is { WindowState: Avalonia.Controls.WindowState.Minimized })
                            logWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        var captured = logWindow?.GetLogSnapshot() ?? Array.Empty<string>();
                        var issue = CrashAnalyzer.Diagnose(captured);
                        string analysis = issue.Title;
                        logWindow?.AppendLog($"[CRASH ANALYZER] {analysis}");
                        UpdateStatus($"Game closed with exit code {exitCode}. {analysis}");
                        TaskDialog.ShowIssue(issue, $"Instance: {target.Name}\nMinecraft: {target.Version} · {target.Loader}\nExit code: {exitCode}\n\n" + string.Join(Environment.NewLine, captured), target.InstancePath);
                        }
                        else
                        {
                            UpdateStatus("Game closed normally.");
                        }
                    });
                    process.Dispose();
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                UpdateStatus("Preparation cancelled. Downloaded files are saved.");
                operation.Cancelled();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Launch error: {ex.Message}");
                operation.Fail(ex);
                logWindow?.AppendLog($"[ERROR]: {ex}");
                Dispatcher.UIThread.Post(() => TaskDialog.ShowIssue(CrashAnalyzer.Diagnose(new[] { ex.ToString() }), ex.ToString(), target.InstancePath));
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsDownloading = false;
                    CanCancelInstallation = false;
                    _installationCancellation = null;
                    TransferDetails = string.Empty;
                });
            }
        });
    }

    private void UpdateStatus(string status, double progress = 0)
    {
        DownloadCenter.Current?.Report(status, progress > 0 ? progress : null);
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = status;
            DownloadProgress = progress;
        });
    }

    private void UpdateTransferProgress(TransferSnapshot snapshot)
    {
        DownloadCenter.Current?.Report(snapshot);
        Dispatcher.UIThread.Post(() =>
        {
            TransferDetails = $"{snapshot.Stage}: {snapshot.Details}";
            DownloadProgress = snapshot.Percent;
        });
    }

    private static async Task<bool> ConfirmCompatibilityAsync(string message)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null) return false;
        var cancel = new Avalonia.Controls.Button { Content = "Cancel" };
        var proceed = new Avalonia.Controls.Button { Content = "Launch anyway" };
        var dialog = new Avalonia.Controls.Window
        {
            Title = "Mod compatibility warnings", Width = 620, Height = 400,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        var panel = new Avalonia.Controls.DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var actions = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 12, Margin = new Thickness(0, 12, 0, 0), Children = { cancel, proceed }
        };
        Avalonia.Controls.DockPanel.SetDock(actions, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(actions);
        panel.Children.Add(new Avalonia.Controls.ScrollViewer
        {
            Content = new Avalonia.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        });
        dialog.Content = panel;
        WindowTitleBar.Apply(dialog);
        cancel.Click += (_, _) => dialog.Close(false);
        proceed.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(desktop.MainWindow);
    }

    [RelayCommand]
    private async Task LaunchSpecificInstanceAsync(InstanceModel? instance)
    {
        if (instance is { CanChangeFiles: false }) return;
        if (instance is { IsInstalled: false })
        {
            await InstallInstanceAsync(instance);
            return;
        }
        await LaunchInstanceAsync(instance);
    }

    public Task LaunchInstanceOnServerAsync(InstanceModel instance, string serverAddress)
        => LaunchInstanceCoreAsync(instance, serverAddress);

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        await LaunchInstanceAsync(SelectedInstance);
    }

    [RelayCommand]
    private void OpenInstanceFolder(InstanceModel? instance)
    {
        if (instance != null) OpenPath(instance.InstancePath);
    }

    [RelayCommand]
    private void OpenSavesFolder(InstanceModel? instance)
    {
        if (instance != null) OpenPath(Path.Combine(instance.InstancePath, "saves"));
    }

    [RelayCommand]
    private void OpenScreenshotsFolder(InstanceModel? instance)
    {
        if (instance != null) OpenPath(Path.Combine(instance.InstancePath, "screenshots"));
    }

    [RelayCommand]
    private void OpenLogsFolder(InstanceModel? instance)
    {
        if (instance != null) OpenPath(Path.Combine(instance.InstancePath, "logs"));
    }

    [RelayCommand]
    private void OpenOptionsFile(InstanceModel? instance)
    {
        if (instance != null) OpenPath(Path.Combine(instance.InstancePath, "options.txt"));
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                PlatformSupport.OpenPath(path);
                return;
            }

            if (!string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
                PlatformSupport.OpenPath(path);
                return;
            }

            Directory.CreateDirectory(path);
            PlatformSupport.OpenPath(path);
        }
        catch { }
    }
}

public class ThemePresetViewModel
{
    public string Name { get; set; }
    public string HexColor { get; set; }
    public IBrush Brush { get; set; }
    public string Background { get; }
    public string Sidebar { get; }
    public string Panel { get; }
    public string Border { get; }
    public string Text { get; }
    public string Subtext { get; }

    public ThemePresetViewModel(string name, string hexColor,
        string background = "#0A0D14", string sidebar = "#0E121B", string panel = "#121722",
        string border = "#1C2433", string text = "#F8FAFC", string subtext = "#64748B")
    {
        Name = name;
        HexColor = hexColor;
        Background = background;
        Sidebar = sidebar;
        Panel = panel;
        Border = border;
        Text = text;
        Subtext = subtext;
        if (Color.TryParse(hexColor, out var color))
        {
            Brush = new SolidColorBrush(color);
        }
        else
        {
            Brush = new SolidColorBrush(Colors.MediumPurple);
        }
    }
}
