using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class InstanceWorkspaceView : UserControl, IDisposable
{
    public InstanceModel Instance { get; }
    public InstanceDetailsViewModel Details { get; }
    public InstanceModsViewModel Mods { get; }
    public string SelectedSection { get; private set; } = "Overview";
    public Action? BackRequested { get; set; }
    public Func<Task>? LaunchAsync { get; set; }
    public Action? CancelMaintenance { get; set; }
    public Action? LauncherSettingsRequested { get; set; }
    public event Action? BusyChanged;
    private readonly Dictionary<string, ContentCatalogView> _catalogs = new();
    private readonly Func<string, DownloadModsViewModel> _catalogFactory;
    private bool _disposed;
    public bool IsDisposed => _disposed;
    public DownloadModsViewModel? CurrentCatalog => CatalogHost.Content is ContentCatalogView view ? view.DataContext as DownloadModsViewModel : null;
    public bool IsBusy => Instance.IsInstalling || Details.IsImporting || Details.RemoveItemCommand.IsRunning || Mods.IsBusy || Details.CheckCompatibilityCommand.IsRunning ||
        Details.RepairInstanceCommand.IsRunning || Details.UpdateVortexClientCommand.IsRunning || _catalogs.Values.Any(c => c.DataContext is DownloadModsViewModel { IsInstalling: true });

    public InstanceWorkspaceView() : this(new InstanceModel()) { }
    public InstanceWorkspaceView(InstanceModel instance, Func<string, Task>? joinServer = null, Func<string, DownloadModsViewModel>? catalogFactory = null, ModUpdateService? modUpdates = null)
    {
        Instance = instance;
        Details = new(instance, joinServer);
        Mods = new(instance.Version, instance.Loader, instance.InstancePath, modUpdates, instance.LoaderVersion) { CanChangeFiles = () => instance.CanChangeFiles && !Details.IsImporting };
        _catalogFactory = catalogFactory ?? (type => new(instance.Version, instance.Loader, instance.InstancePath, type));
        InitializeComponent(); DataContext = this;
        ExtraSections.ItemsSource = new[] { "Screenshots", "Logs", "Options", "Servers" }; ExtraSections.SelectedIndex = 0;
        ModsPage.OverviewRequested = () => SelectSection("Overview");
        ModsPage.AddFilesRequested = async () => { if (IsBusy) return; Details.SelectedSection = "Mods"; await Files.AddFilesAsync(); Mods.ReloadAfterContentChange(); Mods.StatusMessage = Details.StatusMessage; };
        DragDrop.SetAllowDrop(ModsPage, true);
        ModsPage.AddHandler(DragDrop.DragOverEvent, OnModsDragOver);
        ModsPage.AddHandler(DragDrop.DropEvent, OnModsDrop);
        Mods.BrowseContentAsync = () => { OpenCatalog("mod"); return Task.CompletedTask; };
        Mods.DialogOwner = () => Owner();
        Mods.ShowModCheckAsync = report => new ModCheckWindow(report, instance.InstancePath).ShowDialog(Owner());
        Details.ShowModCheckAsync = report => new ModCheckWindow(report, instance.InstancePath).ShowDialog(Owner());
        Mods.ConfirmRemovalAsync = text => new RemoveContentWindow(text).ShowDialog<bool>(Owner());
        Mods.ConfirmUpdatesAsync = count => new TaskDialog("Update selected mods?", $"Replace {count} selected mod(s)? Original files are backed up and disabled mods remain disabled.", "Update selected").ShowDialog<bool>(Owner());
        Details.ConfirmRemovalAsync = text => new RemoveContentWindow(text).ShowDialog<bool>(Owner());
        Details.ShareCallback = () => new SharePackWindow(instance).ShowDialog(Owner());
        Details.ExportReportAsync = () => DiagnosticReportExport.SaveAsync(Owner(), instance.InstancePath);
        Details.OpenModsManagerAsync = updates => { SelectSection("Mods"); Mods.IsPreviewVisible = updates; return Task.CompletedTask; };
        Files.BrowseRequested = () => OpenCatalog(SelectedSection == "Resource packs" ? "resourcepack" : "shader");
        Details.PropertyChanged += OnState; Mods.PropertyChanged += OnState; Instance.PropertyChanged += OnState;
        Details.RemoveItemCommand.PropertyChanged += OnState; Details.RepairInstanceCommand.PropertyChanged += OnState; Details.UpdateVortexClientCommand.PropertyChanged += OnState;
        Details.CheckCompatibilityCommand.PropertyChanged += OnState;
        SelectSection("Overview");
    }
    private Window Owner() => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The instance workspace must be attached to the launcher.");
    private void OnState(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (ReferenceEquals(sender, Instance) || (ReferenceEquals(sender, Details) && e.PropertyName == nameof(InstanceDetailsViewModel.IsImporting))) { Mods.NotifyAvailability(); foreach (var c in _catalogs.Values) (c.DataContext as DownloadModsViewModel)?.NotifyAvailability(); }
        UpdateAvailability(); BusyChanged?.Invoke();
    }
    private void UpdateAvailability()
    {
        WorkspacePlay.IsEnabled = Instance.CanLaunch && !IsBusy && LaunchAsync != null;
        BackToLibrary.IsEnabled = !IsBusy; SectionTabs.IsEnabled = !IsBusy;
        ExtraSections.IsEnabled = !IsBusy;
        ReturnFromCatalog.IsEnabled = !IsBusy;
        BusyBanner.IsVisible = IsBusy;
        BusyStatus.Text = CurrentCatalog?.IsInstalling == true ? CurrentCatalog.StatusMessage : Mods.IsBusy ? Mods.StatusMessage : Details.IsImporting ? "Adding files safely…" : Instance.IsInstalling ? Instance.StatusText : "Finishing the current operation…";
        CancelWorkspaceOperation.IsVisible = CurrentCatalog?.IsInstalling == true || Mods.IsBusy || Details.CheckCompatibilityCommand.IsRunning || (Instance.IsInstalling && CancelMaintenance != null);
        OpenLauncherSettings.IsEnabled = !IsBusy && LauncherSettingsRequested != null;
    }
    public void RefreshAvailability() => UpdateAvailability();
    public void SelectSection(string section)
    {
        if (IsBusy || _disposed) return;
        if (section is not ("Overview" or "Mods" or "Resource packs" or "Shader packs" or "Worlds" or "More files" or "Settings")) return;
        SelectedSection = section; CatalogPage.IsVisible = false;
        OverviewPage.IsVisible = section == "Overview"; ModsPage.IsVisible = section == "Mods"; SettingsPage.IsVisible = section == "Settings";
        FilesPage.IsVisible = section is "Resource packs" or "Shader packs" or "Worlds" or "More files";
        ExtraSections.IsVisible = section == "More files";
        if (FilesPage.IsVisible) Details.SelectedSection = section == "More files" ? ExtraSections.SelectedItem as string ?? "Screenshots" : section;
        if (section == "Mods") { Details.SelectedSection = "Mods"; Mods.RefreshInstalledMods(); }
        foreach (var b in SectionTabs.Children.OfType<Button>()) { if (Equals(b.Tag, section)) b.Classes.Add("active"); else b.Classes.Remove("active"); }
        UpdateAvailability(); UiMotion.Reveal(WorkspaceBody);
    }
    public void OpenCatalog(string type)
    {
        if (type is not ("mod" or "resourcepack" or "shader")) return;
        if (IsBusy || !Instance.CanChangeFiles || !Instance.IsInstalled) return;
        if (!_catalogs.TryGetValue(type, out var view))
        {
            var vm = _catalogFactory(type);
            vm.CanChangeFiles = () => !_disposed && Instance.CanChangeFiles && !Details.IsImporting && !Mods.IsBusy;
            vm.ConfirmDependenciesAsync = dependencies => new DependencyApprovalWindow(dependencies).ShowDialog<bool>(Owner());
            view = new ContentCatalogView { DataContext = vm }; _catalogs.Add(type, view); vm.PropertyChanged += OnCatalogState;
        }
        CatalogHost.Content = view; OverviewPage.IsVisible = ModsPage.IsVisible = FilesPage.IsVisible = SettingsPage.IsVisible = false;
        CatalogPage.IsVisible = true; UiMotion.Reveal(WorkspaceBody);
        UpdateAvailability();
    }
    private void OnCatalogState(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.PropertyName == nameof(DownloadModsViewModel.StatusMessage)) { if (CurrentCatalog?.IsInstalling == true) UpdateAvailability(); return; }
        if (e.PropertyName != nameof(DownloadModsViewModel.IsInstalling)) return;
        if (sender is DownloadModsViewModel { IsInstalling: false }) { Mods.ReloadAfterContentChange(); Details.RefreshCommand.Execute(null); }
        UpdateAvailability(); BusyChanged?.Invoke();
    }
    private void OnTab(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string section }) SelectSection(section); }
    private void OnBack(object? sender, RoutedEventArgs e) { if (!IsBusy) BackRequested?.Invoke(); }
    private async void OnPlay(object? sender, RoutedEventArgs e) { if (!IsBusy && Instance.CanLaunch && LaunchAsync != null) await LaunchAsync(); }
    private void OnInstalled(object? sender, RoutedEventArgs e) => SelectSection(SelectedSection);
    private void OnCancelOperation(object? sender, RoutedEventArgs e)
    {
        if (CurrentCatalog?.IsInstalling == true) CurrentCatalog.CancelInstall();
        else if (Mods.IsBusy) Mods.CancelOperationCommand.Execute(null);
        else if (Details.CheckCompatibilityCommand.IsRunning) Details.CancelModCheck();
        else if (Instance.IsInstalling) CancelMaintenance?.Invoke();
    }
    private void OnLauncherSettings(object? sender, RoutedEventArgs e) { if (!IsBusy) LauncherSettingsRequested?.Invoke(); }
    private void OnModsDragOver(object? sender, DragEventArgs e)
    {
        bool accepted = !IsBusy && Instance.IsInstalled && Instance.CanChangeFiles && e.Data.GetFiles()?.Any(f => f.TryGetLocalPath()?.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) == true) == true;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }
    private async void OnModsDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        await ImportModFilesAsync(e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? Array.Empty<string>());
    }
    public async Task ImportModFilesAsync(IEnumerable<string> paths)
    {
        if (_disposed || IsBusy || !Instance.CanChangeFiles || !Instance.IsInstalled) return;
        Details.SelectedSection = "Mods";
        await Details.ImportFilesAsync(paths);
        Mods.ReloadAfterContentChange(); Mods.StatusMessage = Details.StatusMessage;
    }
    private void OnExtraSection(object? sender, SelectionChangedEventArgs e) { if (SelectedSection == "More files" && ExtraSections.SelectedItem is string section) Details.SelectedSection = section; }
    private void OnFolder(object? sender, RoutedEventArgs e) { try { Core.Launch.PlatformSupport.OpenPath(Instance.InstancePath); } catch (Exception ex) { Details.StatusMessage = ex.Message; } }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Instance.PropertyChanged -= OnState; Details.PropertyChanged -= OnState; Mods.PropertyChanged -= OnState;
        Details.RemoveItemCommand.PropertyChanged -= OnState; Details.RepairInstanceCommand.PropertyChanged -= OnState; Details.UpdateVortexClientCommand.PropertyChanged -= OnState;
        Details.CheckCompatibilityCommand.PropertyChanged -= OnState;
        foreach (var view in _catalogs.Values) if (view.DataContext is DownloadModsViewModel vm) { vm.PropertyChanged -= OnCatalogState; vm.CancelInstall(); vm.CancelSearch(); vm.ConfirmDependenciesAsync = null; }
        UiMotion.Cancel(WorkspaceBody); ModsPage.AddFilesRequested = null; ModsPage.OverviewRequested = null;
        Mods.ShowModCheckAsync = null; Details.ShowModCheckAsync = null;
        Files.Dispose(); Details.Dispose(); Mods.BrowseContentAsync = null; Mods.Dispose(); BusyChanged = null; BackRequested = null; LaunchAsync = null; CancelMaintenance = null; LauncherSettingsRequested = null;
    }
}
