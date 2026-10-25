using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class MainWindow : Window
{
    private void OnOpenDataFolder(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VortexLauncher.Core.Launch.PlatformSupport.DataRoot) { UseShellExecute = true });
    }
    public InstanceWorkspaceView? ActiveWorkspace { get; private set; }
    private readonly Dictionary<Control, CancellationTokenSource> _cardMotions = new();

    private async void OnCardAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control card) return;
        var cancellation = new CancellationTokenSource();
        _cardMotions[card] = cancellation;
        int index = card.DataContext is InstanceModel instance && DataContext is MainWindowViewModel vm
            ? vm.Instances.IndexOf(instance) : 0;
        try { await UiMotion.RevealAsync(card, cancellation.Token, Math.Clamp(index, 0, 6) * 25); }
        finally
        {
            if (_cardMotions.TryGetValue(card, out var current) && ReferenceEquals(current, cancellation))
                _cardMotions.Remove(card);
            cancellation.Dispose();
        }
    }

    private void OnCardDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control card && _cardMotions.Remove(card, out var cancellation)) cancellation.Cancel();
    }

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
            LibraryLocationHint.Text = "Your library is in Vortex Launcher Data beside the installed .app, outside its signed bundle. A new installation folder starts with an empty library; old Application Support data is not connected automatically.";
        WindowTitleBar.Apply(this);
        Height = 800;
        MinHeight = 600;
        foreach (var tab in new[] { libraryNav, modpacksNav, profilesNav, settingsNav }) tab.Click += (_, _) => HideInstanceWorkspace();
        libraryBrowser.PropertyChanged += (_, e) =>
        {
            // Effective visibility and tile layout settle after this change, so start on the next pass.
            if (e.Property == IsVisibleProperty && libraryBrowser.IsVisible)
                Dispatcher.UIThread.Post(AnimateCollectionOpening, DispatcherPriority.Loaded);
        };
        librarySelection.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty && e.NewValue != null)
                Dispatcher.UIThread.Post(AnimateFeaturedCover, DispatcherPriority.Loaded);
        };
    }

    // The collection opens top to bottom: heading, filters, then the visible tiles one after another.
    private void AnimateCollectionOpening()
    {
        if (!libraryBrowser.IsEffectivelyVisible) return;
        var tiles = instanceCards.GetVisualDescendants().OfType<Border>()
            .Where(tile => tile.Classes.Contains("LibraryTile") && tile.IsEffectivelyVisible)
            .Select(tile => (tile, at: tile.TranslatePoint(default, instanceCards) ?? default))
            .Where(item => item.at.Y < instanceCards.Bounds.Height)
            .OrderBy(item => item.at.Y).ThenBy(item => item.at.X)
            .Take(12).Select(item => item.tile).ToArray();
        if (tiles.Length == 0)
        {
            _ = UiMotion.RiseAsync(libraryBrowser, default, 18, duration: 220);
            return;
        }
        _ = UiMotion.RiseAsync(browserHeader, default, 10, duration: 220);
        _ = UiMotion.RiseAsync(browserFilters, default, 10, delay: 40, duration: 220);
        for (int i = 0; i < tiles.Length; i++)
            _ = UiMotion.RiseAsync(tiles[i], default, 16, delay: 70 + i * 30, duration: 260);
    }

    // A newly featured instance: the cover cross-fades in and its title card rises after it.
    private void AnimateFeaturedCover()
    {
        Control? cover = null, card = null;
        foreach (var border in librarySelection.GetVisualDescendants().OfType<Border>())
        {
            if (border.Name == "heroCover") cover = border;
            else if (border.Name == "selectedInstanceBar") card = border;
        }
        if (cover != null) _ = UiMotion.RiseAsync(cover, default, 0, fromOpacity: .45, duration: 320);
        if (card != null) _ = UiMotion.RiseAsync(card, default, 14, delay: 60, duration: 260);
    }

    private void OnQuickProfileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AccountModel account } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectAccountCommand.Execute(account);
            if (vm.ProfileError.Length == 0) profileSwitcher.Flyout?.Hide();
        }
    }

    private void OnManageProfilesClick(object? sender, RoutedEventArgs e)
    {
        profileSwitcher.Flyout?.Hide();
        if (DataContext is MainWindowViewModel vm) vm.SelectTabCommand.Execute("Accounts");
        HideInstanceWorkspace();
    }

    private async void OnOpenWardrobeClick(object? sender, RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm)return;
        HideInstanceWorkspace();
        vm.SetDiscordSection("Wardrobe");
        try { await vm.OpenWardrobeAsync(this); }
        finally { vm.SetDiscordSection(null); }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel startup) _ = startup.InitializeAsync();
        if (startupOverlay.IsVisible) _ = UiMotion.RiseAsync(startupGreeting, default, 22, duration: 420);

        _ = HideStartupGreetingAsync();
    }

    private async Task HideStartupGreetingAsync()
    {
        if (DataContext is MainWindowViewModel startup) await startup.InitializeAsync();
        await Task.Delay(120);
        startupOverlay.Opacity = 0;
        // The launcher settles into place while the greeting fades away above it.
        _ = UiMotion.RiseAsync(mainShell, default, 12, fromOpacity: .6, duration: 340);
        await Task.Delay(250);
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.IsStartupGreetingVisible = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        ActiveWorkspace?.Dispose();
        if (DataContext is MainWindowViewModel vm) { vm.CloseLauncherSessions(); vm.CancelStartup(); vm.Library.Dispose(); }
        UiMotion.Cancel(mainShell);
        foreach (var motion in System.Linq.Enumerable.ToArray(_cardMotions.Values)) motion.Cancel();
        base.OnClosed(e);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only the empty title-bar strip should initiate a window drag.
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && point.Position.Y <= 32)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnAddInstanceClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateInstanceWindow();
        var result = await dialog.ShowDialog<CreateInstanceViewModel?>(this);

        if (result != null && DataContext is MainWindowViewModel mainVm)
        {
            await mainVm.AddInstanceAsync(
                result.InstanceName,
                result.SelectedVersion,
                result.SelectedLoader,
                result.VortexClientEnabled);
        }
    }

    private async void OnImportPackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import Minecraft pack",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Minecraft packs") { Patterns = new[] { "*.zip", "*.mrpack" } }
            }
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path)) await mainVm.ImportInstanceArchive(path);
    }

    private async void OnExportPackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm || !mainVm.HasSelectedInstance) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"{mainVm.SelectedInstance!.Name}.zip",
            Title = "Export Minecraft pack",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } }
            }
        });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) mainVm.ExportSelectedInstance(path);
    }

    private async void OnExportInstanceCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstanceModel instance } ||
            DataContext is not MainWindowViewModel mainVm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"{instance.Name}.zip",
            Title = $"Export {instance.Name}",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } }
            }
        });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) mainVm.ExportInstance(instance, path);
    }

    private async void OnSetCoverClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: InstanceModel instance } ||
            DataContext is not MainWindowViewModel mainVm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose instance cover",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                }
            }
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            instance.CoverImagePath = path;
            mainVm.SaveInstanceMetadata(instance);
        }
    }

    private async void OnSetIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: InstanceModel instance } ||
            DataContext is not MainWindowViewModel mainVm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose instance icon",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.ico" }
                }
            }
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            instance.IconPath = path;
            mainVm.SaveInstanceMetadata(instance);
        }
    }

    private void OnOpenInstanceMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not InstanceModel instance ||
            DataContext is not MainWindowViewModel mainVm)
        {
            return;
        }

        ShowInstanceMenu(button, instance, mainVm);
    }

    private void OnInstanceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: InstanceModel instance } card &&
            e.GetCurrentPoint(card).Properties.IsRightButtonPressed &&
            DataContext is MainWindowViewModel vm)
        {
            e.Handled = true;
            ShowInstanceMenu(card, instance, vm);
        }
    }

    private void OnFeaturedPlayClick(object? sender, RoutedEventArgs e) => UiMotion.Pulse(playPulse);

    // Choosing a tile features it on the cover; arrow-key browsing keeps the collection open.
    private void OnInstanceTileTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
        instanceSwitcher.IsChecked = false;
    }

    private void OnLibraryBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || instanceSwitcher.IsChecked != true) return;
        instanceSwitcher.IsChecked = false;
        instanceSwitcher.Focus();
        e.Handled = true;
    }

    private void ShowInstanceMenu(Control anchor, InstanceModel instance, MainWindowViewModel vm)
    {
        var menu = new MenuFlyout();
        AddMenuItem(menu, instance.IsFavorite ? "Unpin from favorites" : "Pin to favorites", () => vm.ToggleFavoriteCommand.Execute(instance));
        AddMenuItem(menu, "Rename", () => RenameInstanceAsync(instance, vm));
        AddMenuItem(menu, "Clone instance", () => vm.CloneInstanceCommand.Execute(instance),
            instance.CanChangeFiles && !vm.IsGlobalLoading && !vm.IsDownloading);
        AddMenuItem(menu, "Export", () => OnExportInstanceCardClick(new Button { Tag = instance }, new RoutedEventArgs()), instance.CanChangeFiles);
        AddMenuItem(menu, "Share pack…", () => new SharePackWindow(instance).Show(this), instance.CanChangeFiles);
        AddMenuItem(menu, "Update Vortex Client", () => vm.UpdateVortexClientCommand.Execute(instance),
            instance.CanChangeFiles && !vm.IsGlobalLoading && !vm.IsDownloading && VortexLauncher.Core.Network.VortexClientService.IsCompatible(instance.Version, instance.Loader));
        AddMenuItem(menu, "Repair instance", () => vm.RepairInstanceCommand.Execute(instance),
            instance.CanChangeFiles && !vm.IsGlobalLoading && !vm.IsDownloading);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "View Folder", () => vm.OpenInstanceFolderCommand.Execute(instance));
        AddMenuItem(menu, "Set cover", () => OnSetCoverClick(new Button { DataContext = instance }, new RoutedEventArgs()));
        AddMenuItem(menu, "Set icon", () => OnSetIconClick(new Button { DataContext = instance }, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Delete", () => vm.DeleteInstanceCommand.Execute(instance), instance.CanChangeFiles);
        menu.ShowAt(anchor);
    }

    private async void RenameInstanceAsync(InstanceModel instance, MainWindowViewModel vm)
    {
        var dialog = new RenameInstanceWindow(instance.Name);
        var name = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(name)) vm.RenameInstance(instance, name);
    }

    private void OnManageInstanceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstanceModel instance }) OpenInstanceWorkspace(instance);
    }

    public void OpenInstanceWorkspace(InstanceModel instance, string? section = null)
    {
        if (ActiveWorkspace?.IsBusy == true) return;
        if (!ReferenceEquals(ActiveWorkspace?.Instance, instance))
        {
            ActiveWorkspace?.Dispose();
            var main = DataContext as MainWindowViewModel;
            var workspace = new InstanceWorkspaceView(instance, main == null ? null : address => main.LaunchInstanceOnServerAsync(instance, address));
            ActiveWorkspace = workspace;
            workspace.BackRequested = HideInstanceWorkspace;
            workspace.BusyChanged += OnWorkspaceBusy;
            if (main != null)
            {
                main.SelectedInstance = instance;
                workspace.LaunchAsync = () => main.LaunchSpecificInstanceCommand.ExecuteAsync(instance);
                workspace.Details.RepairCallback = async () => { await main.RepairInstanceCommand.ExecuteAsync(instance); workspace.Details.StatusMessage = main.StatusMessage; };
                workspace.Details.ClientUpdateCallback = async () => { await main.UpdateVortexClientCommand.ExecuteAsync(instance); workspace.Details.StatusMessage = main.StatusMessage; };
                workspace.CancelMaintenance = () => main.CancelInstallationCommand.Execute(null);
                workspace.LauncherSettingsRequested = () => { HideInstanceWorkspace(); main.SelectTabCommand.Execute("Settings"); };
            }
            InstanceWorkspaceHost.Content = workspace;
        }
        if (DataContext is MainWindowViewModel viewModel) viewModel.SelectTabCommand.Execute("Instances");
        LibraryAndPages.IsVisible = false; InstanceWorkspaceHost.IsVisible = true;
        (DataContext as MainWindowViewModel)?.SetDiscordSection("Instance");
        ActiveWorkspace!.SelectSection(section ?? ActiveWorkspace.SelectedSection); ActiveWorkspace.RefreshAvailability();
    }
    public void HideInstanceWorkspace()
    {
        if (ActiveWorkspace?.IsBusy == true) return;
        InstanceWorkspaceHost.IsVisible = false; LibraryAndPages.IsVisible = true;
        (DataContext as MainWindowViewModel)?.SetDiscordSection(null);
    }
    private void OnWorkspaceBusy()
    {
        bool enabled = ActiveWorkspace?.IsBusy != true;
        foreach (var tab in new[] { libraryNav, modpacksNav, profilesNav, settingsNav }) tab.IsEnabled = enabled;
        if (activePlayerCard.Parent is Button profile) profile.IsEnabled = enabled;
    }
    private void OnWorkspaceShortcut(object? sender, RoutedEventArgs e)
    { if (sender is Button { Tag: InstanceModel instance, CommandParameter: string section }) OpenInstanceWorkspace(instance, section); }

    private static void AddMenuItem(MenuFlyout menu, string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (header == "Delete") item.Classes.Add("danger");
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnThemePresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ThemePresetViewModel preset } &&
            DataContext is MainWindowViewModel mainVm)
        {
            mainVm.ApplyPreset(preset);
            mainVm.StatusMessage = $"Theme applied: {preset.Name}";
        }
    }
}
