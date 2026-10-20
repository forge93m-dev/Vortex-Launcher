using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VortexLauncher.Core.Network;

namespace VortexLauncher.ViewModels;

public partial class CreateInstanceViewModel : ObservableObject
{
    private readonly MinecraftService _minecraftService = new();
    private readonly ForgeService _forgeService = new();
    private readonly QuiltService _quiltService = new();

    [ObservableProperty]
    private string _instanceName = "New Instance";

    [ObservableProperty]
    private string _selectedVersion = "1.20.4";

    [ObservableProperty]
    private LoaderOption? _selectedLoaderOption;

    [ObservableProperty]
    private bool _isLoadingVersions = true;

    [ObservableProperty]
    private bool _isCheckingForge;

    [ObservableProperty]
    private bool _vortexClientEnabled;

    public bool CanUseVortexClient => VortexClientService.IsCompatible(SelectedVersion, SelectedLoader);
    public string VortexClientDescription => CanUseVortexClient
        ? VortexClientService.IsExperimentalTarget(SelectedVersion)
            ? $"Experimental client port for Minecraft {SelectedVersion}. Automated compatibility checks passed; in-game HUD and wardrobe testing is still required."
            : "FPS Counter, smooth Zoom and the Vortex module menu. No extra libraries required."
        : "Available for " + VortexClientService.SupportedVersionsDescription + ". Select a supported configuration to enable it.";
    public string ConfigurationSummary => $"Minecraft {SelectedVersion} · {SelectedLoader}";
    public string VortexClientStatus => VortexClientEnabled ? "Vortex Enabled" : "Optional";

    partial void OnVortexClientEnabledChanged(bool value) => OnPropertyChanged(nameof(VortexClientStatus));

    private void RefreshVortexClient()
    {
        if (!CanUseVortexClient) VortexClientEnabled = false;
        OnPropertyChanged(nameof(CanUseVortexClient));
        OnPropertyChanged(nameof(VortexClientDescription));
        OnPropertyChanged(nameof(ConfigurationSummary));
    }

    public ObservableCollection<string> AvailableVersions { get; } = new();

    public ObservableCollection<LoaderOption> AvailableLoaders { get; } = new();

    public string SelectedLoader => SelectedLoaderOption?.Name ?? "Fabric";

    public bool CanCreate => SelectedLoaderOption?.IsAvailable == true &&
                             (!IsCheckingForge || !string.Equals(
                                 SelectedLoader, "Forge", StringComparison.OrdinalIgnoreCase));

    public CreateInstanceViewModel()
    {
        AvailableLoaders.Add(new LoaderOption("Vanilla"));
        AvailableLoaders.Add(new LoaderOption("Fabric"));
        AvailableLoaders.Add(new LoaderOption("Forge", false));
        // NeoForge is intentionally disabled until its installer/profile
        // format has a dedicated implementation. It must never be routed to
        // Forge because the two loaders are not interchangeable.
        AvailableLoaders.Add(new LoaderOption("NeoForge", false, "NeoForge (not supported yet)"));
        AvailableLoaders.Add(new LoaderOption("Quilt", false));
        SelectedLoaderOption = AvailableLoaders[1];
        _ = LoadVersionsAsync();
    }

    partial void OnSelectedLoaderOptionChanged(LoaderOption? value)
    {
        if (value is { IsAvailable: false })
        {
            SelectedLoaderOption = AvailableLoaders.FirstOrDefault(loader => loader.IsAvailable);
            return;
        }

        OnPropertyChanged(nameof(SelectedLoader));
        OnPropertyChanged(nameof(CanCreate));
        RefreshVortexClient();
    }

    partial void OnIsCheckingForgeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnSelectedVersionChanged(string value)
    {
        RefreshVortexClient();
        _ = RefreshForgeAvailabilityAsync(value);
    }

    private async Task LoadVersionsAsync()
    {
        IsLoadingVersions = true;
        var versions = await _minecraftService.GetReleaseVersionsAsync();

        AvailableVersions.Clear();
        foreach (var version in versions)
        {
            AvailableVersions.Add(version);
        }

        if (AvailableVersions.Count > 0)
        {
            SelectedVersion = AvailableVersions[0];
        }

        IsLoadingVersions = false;
        await RefreshLoaderAvailabilityAsync(SelectedVersion);
    }

    private async Task RefreshForgeAvailabilityAsync(string? minecraftVersion)
        => await RefreshLoaderAvailabilityAsync(minecraftVersion);

    private async Task RefreshLoaderAvailabilityAsync(string? minecraftVersion)
    {
        var forge = AvailableLoaders.FirstOrDefault(loader =>
            string.Equals(loader.Name, "Forge", StringComparison.OrdinalIgnoreCase));
        var quilt = AvailableLoaders.FirstOrDefault(loader =>
            string.Equals(loader.Name, "Quilt", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(minecraftVersion)) return;

        IsCheckingForge = true;
        if (forge != null)
        {
            forge.IsAvailable = false;
            forge.DisplayName = "Forge (checking...)";
            forge.ItemOpacity = 0.42;
        }
        if (quilt != null)
        {
            quilt.IsAvailable = false;
            quilt.DisplayName = "Quilt (checking...)";
            quilt.ItemOpacity = 0.42;
        }

        try
        {
            var forgeTask = _forgeService.GetPromotedForgeVersionAsync(minecraftVersion);
            var quiltTask = _quiltService.GetLatestLoaderVersionAsync(minecraftVersion);
            await Task.WhenAll(forgeTask, quiltTask);

            if (forge != null)
            {
                forge.IsAvailable = !string.IsNullOrWhiteSpace(forgeTask.Result);
                forge.DisplayName = forge.IsAvailable ? "Forge" : "Forge (unavailable)";
                forge.ItemOpacity = forge.IsAvailable ? 1.0 : 0.42;
            }
            if (quilt != null)
            {
                quilt.IsAvailable = !string.IsNullOrWhiteSpace(quiltTask.Result);
                quilt.DisplayName = quilt.IsAvailable ? "Quilt" : "Quilt (unavailable)";
                quilt.ItemOpacity = quilt.IsAvailable ? 1.0 : 0.42;
            }
        }
        catch
        {
            if (forge != null)
            {
                forge.IsAvailable = false;
                forge.DisplayName = "Forge (unavailable)";
                forge.ItemOpacity = 0.42;
            }
            if (quilt != null)
            {
                quilt.IsAvailable = false;
                quilt.DisplayName = "Quilt (unavailable)";
                quilt.ItemOpacity = 0.42;
            }
        }
        finally
        {
            IsCheckingForge = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }
}

public partial class LoaderOption : ObservableObject
{
    public LoaderOption(string name, bool isAvailable = true, string? displayName = null)
    {
        Name = name;
        DisplayName = displayName ?? name;
        IsAvailable = isAvailable;
        ItemOpacity = isAvailable ? 1.0 : 0.42;
    }

    public string Name { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private double _itemOpacity;
}
