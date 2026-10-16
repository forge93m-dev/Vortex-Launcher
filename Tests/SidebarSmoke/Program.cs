using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VortexLauncher.Core.Theme;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Core.Network;
using VortexLauncher.Views;

if (args.Contains("--native-preview"))
{
    AppBuilder.Configure<GameLibraryPreview>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    return;
}

// Headless platform + fixture data: no real accounts, settings, instances or network calls.
AppBuilder.Configure<VortexLauncher.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
ThemeManager.SetTheme("Test", "#F5C2E7", "#11111B", "#181825", "#1E1E2E", "#313244", "#CDD6F4", "#A6ADC8");
var fixture = new SidebarFixture(args.Contains("--benchmark") ? 500 : 0);
var benchmarkClock = Stopwatch.StartNew();
var window = new MainWindow { DataContext = fixture };
window.Show();
if (args.Contains("--benchmark"))
{
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    benchmarkClock.Stop();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { instances = fixture.Instances.Count,
        constructionAndLayoutMs = benchmarkClock.Elapsed.TotalMilliseconds,
        realizedCards = window.GetVisualDescendants().OfType<Border>().Count(c => c.Classes.Contains("InstanceCard")),
        managedBytes = GC.GetTotalMemory(false), processBytes = Process.GetCurrentProcess().WorkingSet64 }));
    window.Close(); return;
}
Pump(600);
var output = Directory.CreateDirectory(Path.Combine(FixturePaths.TempRoot(), "vortex-sidebar-smoke-" + Guid.NewGuid().ToString("N"))).FullName;

VortexLauncher.Core.Media.ArtworkCache.DiskRoot = Path.Combine(output, "artwork-cache");

try
{
    if (args.Contains("--motion")) { MotionRegressionChecks.Run(Pump); return; }
    if (args.Contains("--chrome"))
    {
        ChromeAndHoverChecks.Run(Pump, output);
        Console.WriteLine("Rendered previews: " + output);
        return;
    }
    GameLibraryChecks.Run(window, fixture, Pump, output);
    if (args.Contains("--library")) { Console.WriteLine("Rendered previews: " + output); return; }
    WorkspaceMotionChecks.Run(window, fixture, Pump, output);
    MotionRegressionChecks.Run(Pump);
    if (args.Contains("--workspace")) { Console.WriteLine("Rendered previews: " + output); return; }
    foreach (var tab in new[] { "Modpacks", "Accounts", "Settings" })
    {
        fixture.Select(tab); Pump(250);
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, tab.ToLowerInvariant() + "-redesigned.png"));
        window.Width = 960; window.Height = 600; Pump(150);
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, tab.ToLowerInvariant() + "-minimum.png"));
        window.Width = 1280; window.Height = 800; Pump(150);
    }
    fixture.Select("Modpacks"); Pump(150);
    var packVersion = window.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, fixture.ModpacksVm.ModpackVersions));
    Check(packVersion.SelectedItem == fixture.ModpacksVm.SelectedVersion, "modpack version binding survives redesign");
    packVersion.SelectedIndex = 1; Pump(30);
    Check(fixture.ModpacksVm.SelectedVersion == fixture.ModpacksVm.ModpackVersions[1], "modpack version selection is still two-way");
    packVersion.IsDropDownOpen = true; Pump(180);
    Check(packVersion.IsDropDownOpen, "modpack version dropdown opens");
    packVersion.IsDropDownOpen = false;
    fixture.ModpacksVm.SelectedModpack = fixture.ModpacksVm.Modpacks[2];
    fixture.Select("Instances"); fixture.Select("Modpacks"); Pump(100);
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, "modpack-long-title.png"));
    fixture.Select("Instances");
    window.Width = 960; window.Height = 600; Pump(180);
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-minimum.png"));
    window.Width = 1280; window.Height = 800; Pump(120);

    var create = new CreateInstanceWindow(new CreateFixture());
    Preview(create, "create-instance", output);
    var createVersions = create.GetVisualDescendants().OfType<ComboBox>().First();
    createVersions.SelectedIndex = 1; Pump(30);
    Check(((CreateFixture)create.DataContext!).SelectedVersion == "1.21.11", "create-instance version selector is two-way");
    create.Close();
    foreach (var title in new[] { "Mods", "Resource packs", "Shaders" })
    {
        var content = new DownloadModsWindow { DataContext = new CatalogFixture { ContentTitle = title } };
        Preview(content, title.Replace(" ", "-").ToLowerInvariant() + "-browser", output);
        content.Width = 780; content.Height = 560; Pump(120);
        CheckFits(content, content.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Install selected"), "content install footer fits minimum size");
        content.CaptureRenderedFrame()!.Save(Path.Combine(output, title.Replace(" ", "-").ToLowerInvariant() + "-minimum.png"));
        var version = content.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, ((CatalogFixture)content.DataContext!).SelectedMod.AvailableVersions));
        version.SelectedIndex = 1; Pump(20);
        CheckFits(content, version, title + " version selector fits the smallest window");
        Check(((CatalogFixture)content.DataContext!).SelectedMod.SelectedVersion == ((CatalogFixture)content.DataContext!).SelectedMod.AvailableVersions[1],
            title + " version picker updates selected version");
        content.Close();
    }
    var rename = new RenameInstanceWindow("Survival");
    Preview(rename, "rename", output);
    rename.FindControl<TextBox>("NameInput")!.Text = "   "; Pump(30);
    Check(!rename.FindControl<Button>("SaveButton")!.IsEnabled, "blank rename is rejected");
    rename.FindControl<TextBox>("NameInput")!.Text = "New survival"; Pump(30);
    Check(rename.FindControl<Button>("SaveButton")!.IsEnabled, "valid rename enables save");
    rename.Close();
    var approval = new DependencyApprovalWindow(new[] {
        new ModInstallEntry("fabric-api", "Fabric API", new ModrinthVersionItem { Name = "Fabric API", VersionNumber = "0.140.0" }, true, "Example mod"),
        new ModInstallEntry("lib", "Required library", new ModrinthVersionItem { Name = "Library", VersionNumber = "2.3.0" }, true, "Example mod") });
    Preview(approval, "dependency-approval", output);
    Check(approval.FindControl<Button>("CancelButton")!.IsEffectivelyVisible && approval.FindControl<Button>("ApproveButton")!.IsEffectivelyVisible,
        "dependency permission choices stay visible");
    approval.Close();
    var console = new DebugLogWindow();
    console.AppendLog("[INFO] Vortex Client initialized.");
    console.AppendLog("[WARN] Example warning, not a crash.");
    Preview(console, "game-console", output);
    console.Close();
    var tempInstance = new InstanceModel { Name = "Survival", Version = "26.3", Loader = "Fabric", IsInstalled = true, InstancePath = Path.Combine(output, "fixture-instance") };
    var manager = new InstanceDetailsWindow(tempInstance);
    Preview(manager, "instance-manager-empty", output);
    var managerVm = (InstanceDetailsViewModel)manager.DataContext!;
    foreach (var section in new[] { "Resource packs", "Shader packs", "Servers", "Screenshots" })
    {
        managerVm.SelectedSection = section; Pump(260);
        Check(manager.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("SideButton") && Equals(b.Content, section)).Classes.Contains("active"),
            section + " highlights the selected manager section");
        manager.CaptureRenderedFrame()!.Save(Path.Combine(output, "manager-" + section.Replace(" ", "-").ToLowerInvariant() + ".png"));
    }
    managerVm.SelectedSection = "Mods";
    managerVm.Items.Add(new InstanceContentItem("An extremely long mod name to check file-row overflow", "Compatible with Fabric", "2.0", "test.jar", true, true));
    Pump(260);
    manager.CaptureRenderedFrame()!.Save(Path.Combine(output, "instance-manager-populated.png"));
    manager.Width = 860; manager.Height = 600; Pump(260);
    manager.CaptureRenderedFrame()!.Save(Path.Combine(output, "manager-minimum.png"));
    foreach (var (section, folder, file) in new[] {
        ("Mods", "mods", "example.jar.disabled"),
        ("Resource packs", "resourcepacks", "example.zip"),
        ("Shader packs", "shaderpacks", "example.zip") })
    {
        string directory = Directory.CreateDirectory(Path.Combine(tempInstance.InstancePath, folder)).FullName;
        string filePath = Path.Combine(directory, file);
        File.WriteAllText(filePath, "fixture content " + section);
        managerVm.SelectedSection = section;
        managerVm.RefreshCommand.Execute(null); Pump(50);
        var item = managerVm.Items.Single();
        var remove = manager.GetVisualDescendants().OfType<Button>().Single(b =>
            AutomationProperties.GetName(b) == "Remove file" && b.IsEffectivelyVisible);
        CheckFits(manager, remove, section + " removal button fits the minimum window");
        Check(remove.IsEnabled, section + " removal button is enabled while idle");

        RemoveContentWindow? prompt = null;
        managerVm.ConfirmRemovalAsync = name => {
            prompt = new RemoveContentWindow(name);
            return prompt.ShowDialog<bool>(manager);
        };
        var cancelled = managerVm.RemoveItemCommand.ExecuteAsync(item); Pump(320);
        Check(!managerVm.CanRemoveFiles && !managerVm.CanImportFiles, "pending removal blocks conflicting edits");
        prompt!.CaptureRenderedFrame()!.Save(Path.Combine(output, "remove-confirmation.png"));
        Click(prompt!, prompt!.FindControl<Button>("CancelButton")!); Pump(60);
        cancelled.GetAwaiter().GetResult();
        Check(File.Exists(filePath), section + " cancellation leaves the file untouched");

        managerVm.ConfirmRemovalAsync = _ => {
            tempInstance.IsRunning = true;
            return Task.FromResult(true);
        };
        managerVm.RemoveItemCommand.ExecuteAsync(item).GetAwaiter().GetResult();
        Check(File.Exists(filePath) && !managerVm.CanRemoveFiles, "game starting during confirmation prevents removal");
        tempInstance.IsRunning = false;

        managerVm.ConfirmRemovalAsync = name => {
            prompt = new RemoveContentWindow(name);
            return prompt.ShowDialog<bool>(manager);
        };
        var approved = managerVm.RemoveItemCommand.ExecuteAsync(item); Pump(320);
        Click(prompt!, prompt!.FindControl<Button>("RemoveButton")!); Pump(60);
        approved.GetAwaiter().GetResult();
        string backup = Directory.GetFiles(Path.Combine(tempInstance.InstancePath, ".vortex-trash", folder), "*", SearchOption.AllDirectories).Single();
        Check(!File.Exists(filePath) && managerVm.IsEmpty && File.ReadAllText(backup) == "fixture content " + section,
            section + " confirmed removal preserves exact file contents in backup");
        File.WriteAllText(filePath, "second copy");
        CoreRemoval(filePath, section);
        Check(Directory.GetFiles(Path.Combine(tempInstance.InstancePath, ".vortex-trash", folder), "*", SearchOption.AllDirectories).Length == 2,
            "repeated removals never overwrite an earlier backup");
    }
    string outside = Path.Combine(output, "outside.jar");
    File.WriteAllText(outside, "not an instance file");
    bool rejected = false;
    try { CoreRemoval(outside, "Mods"); } catch (IOException) { rejected = true; }
    Check(rejected && File.Exists(outside), "removal rejects files outside the selected instance folder");
    managerVm.SelectedSection = "Worlds";
    Check(!managerVm.CanRemoveFiles, "world deletion is not exposed by the content removal feature");

    void CoreRemoval(string path, string section) => VortexLauncher.Core.Management.InstanceContentRemoval.MoveToBackup(tempInstance.InstancePath, section, path);
    manager.Close();
    var installedVm = new InstanceModsViewModel("26.3", "Fabric", tempInstance.InstancePath);
    var installed = new InstanceModsWindow { DataContext = installedVm };
    Preview(installed, "installed-mods-empty", output);
    Check(installedVm.IsEmpty, "installed mods empty state tracks real collection");
    installedVm.InstalledMods.Add(new ModModel { Name = "Example mod", FileName = "an-extremely-long-mod-filename-for-overflow-testing-26.3-fabric.jar", IsEnabled = true });
    Pump(50);
    Check(!installedVm.IsEmpty, "installed mods empty state hides after adding an item");
    installed.CaptureRenderedFrame()!.Save(Path.Combine(output, "installed-mods-populated.png"));
    string legacyPath = Path.Combine(tempInstance.InstancePath, "mods", "legacy.jar");
    File.WriteAllText(legacyPath, "legacy fixture");
    installedVm.RefreshInstalledMods();
    var legacyMod = installedVm.InstalledMods.Single();
    installedVm.ConfirmRemovalAsync = _ => Task.FromResult(false);
    installedVm.RemoveModCommand.ExecuteAsync(legacyMod).GetAwaiter().GetResult();
    Check(File.Exists(legacyPath), "installed-mods window also requires removal permission");
    installedVm.CanChangeFiles = () => false;
    installedVm.ConfirmRemovalAsync = _ => Task.FromResult(true);
    installedVm.RemoveModCommand.ExecuteAsync(legacyMod).GetAwaiter().GetResult();
    Check(File.Exists(legacyPath), "installed-mods removal respects the busy-instance guard");
    installedVm.CanChangeFiles = () => true;
    installedVm.RemoveModCommand.ExecuteAsync(legacyMod).GetAwaiter().GetResult();
    Check(!File.Exists(legacyPath) && installedVm.IsEmpty, "installed-mods window uses recoverable removal too");
    installed.Close();
    using var live = new InstanceLibraryViewModel(new ObservableCollection<InstanceModel>());
    Check(live.IsEmpty && !live.HasInstances, "fresh library uses create-instance empty state");
    fixture.Select("Settings"); Pump(80);
    ThemeManager.SetAccentColor("#50FA7B"); Pump(160);
    Check(Application.Current!.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().Single().Palettes[Avalonia.Styling.ThemeVariant.Dark].Accent == Color.Parse("#50FA7B"),
        "native controls track a live accent change");
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, "settings-live-accent.png"));
    ThemeManager.SetTheme("Tokyo Night", "#7AA2F7", "#131722", "#191E2B", "#202636", "#344059", "#DFE6F3", "#A0ADC4");
    fixture.Select("Instances");
    window.MouseMove(new Point(1100, 700)); Pump(400);
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-tokyo-night.png"));
    bool taskCancelled = false;
    using var downloadTask = VortexLauncher.Core.Management.DownloadCenter.Begin("Fixture install: long instance name", () => taskCancelled = true);
    downloadTask.Report("Minecraft files", 47, "180 / 400 files · 28 MB / 70 MB · 12 MB/s · ~4s remaining");
    var downloads = new DownloadsWindow();
    Preview(downloads, "downloads-center", output);
    var jobView = downloads.FindControl<ItemsControl>("tasks")!;
    Check(ReferenceEquals(jobView.ItemsSource, VortexLauncher.Core.Management.DownloadCenter.Operations), "downloads window uses the shared live operation collection");
    downloads.Width = 600; downloads.Height = 440; Pump(80);
    var cancelTask = downloads.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Cancel");
    CheckFits(downloads, cancelTask, "download cancellation fits the small window");
    Click(downloads, cancelTask); Pump(30);
    Check(taskCancelled && downloadTask.State == "Stopping" && !downloadTask.CanCancel, "download cancellation reaches the operation and cannot repeat");
    downloadTask.Cancelled(); Pump(30);
    Check(downloadTask.State == "Cancelled" && !downloadTask.Indeterminate, "cancelled task remains in session history with a final state");
    using var failedTask = VortexLauncher.Core.Management.DownloadCenter.Begin("Fixture: failed loader download");
    failedTask.Fail(new IOException("Library checksum mismatch. Retry from Repair instance.")); Pump(30);
    Check(failedTask.State == "Failed" && failedTask.Details.Contains("checksum"), "failed tasks preserve readable error details");
    using var successfulTask = VortexLauncher.Core.Management.DownloadCenter.Begin("Fixture: client update");
    successfulTask.Complete("Previous client JAR backed up.");
    successfulTask.Report("Late progress callback", 2); Pump(30);
    Check(successfulTask.State == "Completed" && successfulTask.Percent == 100, "late progress cannot revive or regress a completed operation");
    downloads.CaptureRenderedFrame()!.Save(Path.Combine(output, "downloads-history.png"));
    downloads.Close();
    var importPreview = new TaskDialog("Import preview", "Survival\nMinecraft 26.3 · Fabric 0.19.5\nFormat: PolyMC\n\nMods: 12\nResource packs: 2\nWorlds: 1\n\nExisting instances will not be overwritten.", "Import instance");
    Preview(importPreview, "import-preview", output);
    Check(importPreview.GetVisualDescendants().OfType<Button>().Any(b => (b.Content as string) == "Import instance"), "import preview exposes explicit approval");
    importPreview.Close();
    var issueDialog = new TaskDialog("Missing or damaged game component", "A required file could not be read.\n\nWhat to do: try Repair instance. User mods and worlds are preserved.", details: "Exit code: 1\nCaused by: ZipException: invalid LOC header");
    Preview(issueDialog, "launch-error", output);
    issueDialog.Width = 620; issueDialog.Height = 460; Pump(60);
    CheckFits(issueDialog, issueDialog.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Copy details"), "diagnostic copy button remains visible in a small dialog");
    Check(issueDialog.GetVisualDescendants().OfType<WindowTitleBar>().Any(), "new diagnostic windows share the custom title bar");
    issueDialog.Close();
    fixture.SelectTabCommand.Execute("Settings"); Pump(60);
    window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3; Pump(280);
    var updateCheckButton = window.FindControl<Button>("CheckLauncherUpdatesButton")!;
    Check(updateCheckButton.Command != null, "manual update check is bound to a real command");
    Click(window, updateCheckButton); Pump(20);
    Check(fixture.UpdateCheckCount == 1, "manual update check routes through the intended command");
    fixture.UpdateAvailable = true; fixture.Notify("UpdateAvailable"); Pump(60);
    var installUpdateButton = window.FindControl<Button>("InstallLauncherUpdateButton")!;
    Check(installUpdateButton.Command != null && installUpdateButton.IsEffectivelyVisible, "available launcher update exposes the install action");
    Click(window, installUpdateButton); Pump(20);
    Check(fixture.UpdateInstallCount == 1, "update install button routes through InstallLauncherUpdateCommand rather than a missing command");
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, "launcher-updates.png"));
    fixture.UpdateAvailable = false; fixture.Notify("UpdateAvailable");
    Console.WriteLine("LAUNCHER_UPDATE_UI_OK");
    StabilityUiChecks.Run(Pump, output);
    ModManagementUiChecks.Run(Pump, output);
    ChromeAndHoverChecks.Run(Pump, output);
    Console.WriteLine("MAINTENANCE_UI_OK");
    Console.WriteLine("REDESIGN_UI_OK");
    Console.WriteLine("Rendered previews: " + output);
}
finally { window.Close(); }

static void Click(Window window, Control control)
{
    var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    window.MouseMove(center); Pump(60);
    window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left);
}
static void Preview(Window window, string name, string output)
{
    window.Show(); Pump(320);
    Check(window.SystemDecorations == SystemDecorations.None, name + " uses a custom title bar");
    window.CaptureRenderedFrame()!.Save(Path.Combine(output, name + ".png"));
}
static void CheckFits(Window window, Control control, string label)
{
    var point = control.TranslatePoint(new Point(0, 0), window)!.Value;
    Check(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1
        && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1, label);
}
static void Check(bool passed, string description)
{
    if (!passed) throw new InvalidOperationException("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}
static void Pump(int milliseconds)
{
    var elapsed = Stopwatch.StartNew();
    while (elapsed.ElapsedMilliseconds < milliseconds)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(5);
    }
    Dispatcher.UIThread.RunJobs();
}

sealed class SidebarFixture : INotifyPropertyChanged
{
    public VortexLauncher.Core.Management.ResettableCollection<VortexLauncher.Core.Management.InterruptedOperation> InterruptedOperations { get; } = new();
    public bool HasInterruptedOperations => InterruptedOperations.Count > 0;
    public int ResumeCount { get; private set; }
    public int CancelCount { get; private set; }
    public ICommand ResumeInterruptedCommand => new FixtureCommand(_ => ResumeCount++);
    public ICommand CancelInterruptedCommand => new FixtureCommand(_ => CancelCount++);
    public ICommand OpenInterruptedFolderCommand => new FixtureCommand(_ => { });
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ActiveTab { get; private set; } = "Instances";
    public bool IsInstancesTab => ActiveTab == "Instances";
    public bool IsModpacksTab => ActiveTab == "Modpacks";
    public bool IsAccountsTab => ActiveTab == "Accounts";
    public bool IsSettingsTab => ActiveTab == "Settings";
    public bool IsStartupGreetingVisible => false;
    public bool IsInitializing => false;
    public bool IsGlobalLoading => false;
    public bool CanCancelInstallation => false;
    public bool UpdateAvailable { get; set; }
    public bool IsCheckingUpdates => false;
    public bool IsInstallingUpdate => false;
    public string LauncherVersionLabel => "Vortex Launcher 0.1.2 · osx-arm64";
    public string LauncherUpdateStatus => "Vortex Launcher 0.1.3 is available for osx-arm64.";
    public string LauncherUpdateNotes => "Platform-specific self-updates and preserved worlds/settings.";
    public string LastLauncherUpdateResult => "";
    public string UpdateSource => "https://raw.githubusercontent.com/forge93m-dev/Vortex-Launcher/main/latest.json";
    public int UpdateCheckCount { get; private set; }
    public int UpdateInstallCount { get; private set; }
    public ICommand CheckLauncherUpdatesCommand => new FixtureCommand(_ => UpdateCheckCount++);
    public ICommand InstallLauncherUpdateCommand => new FixtureCommand(_ => UpdateInstallCount++);
    public int DownloadsOpenCount { get; private set; }
    public ICommand ShowDownloadsCommand => new FixtureCommand(_ => DownloadsOpenCount++);
    public bool HasSelectedInstance => false;
    public string StatusMessage => "Ready";
    public AccountModel ActiveAccount { get; } = new() { Username = "Steve" };
    public ObservableCollection<AccountModel> Accounts { get; } = new();
    public ObservableCollection<InstanceModel> Instances { get; } = new()
    {
        new() { Name = "Survival", Version = "26.3", Loader = "Fabric", IsInstalled = true },
        new() { Name = "Creative", Version = "1.21.11", Loader = "Vanilla", IsInstalled = true },
        new() { Name = "An instance with a deliberately very long name", Version = "1.20.1", Loader = "Forge", IsInstalled = true }
    };
    public ICommand SelectTabCommand { get; }
    public InstanceLibraryViewModel Library { get; }
    public string PageEyebrow => ActiveTab == "Instances" ? "YOUR COLLECTION" : "VORTEX LAUNCHER";
    public string PageTitle => ActiveTab switch { "Modpacks" => "Discover modpacks", "Accounts" => "Player profiles", "Settings" => "Launcher settings", _ => "Your library" };
    public string PageDescription => "All your worlds, ready when you are.";
    public int RamGb { get; set; } = 6;
    public string CustomJavaPath { get; set; } = "";
    public string JvmArgs { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";
    public bool MinimizeOnGameLaunch { get; set; } = true;
    public bool InterfaceAnimationsEnabled { get; set; } = true;
    public string AccentColor { get; set; } = "#F5C2E7";
    public string NewAccountUsername { get; set; } = "";
    public ObservableCollection<ThemePresetViewModel> ThemePresets { get; } = new()
    {
        new("Tokyo Night", "#7AA2F7", "#131722", "#191E2B", "#202636", "#344059", "#DFE6F3", "#A0ADC4"),
        new("Catppuccin", "#F5C2E7", "#11111B", "#181825", "#1E1E2E", "#313244", "#CDD6F4", "#A6ADC8"),
        new("Nord", "#88C0D0", "#2E3440", "#3B4252", "#434C5E", "#4C566A", "#ECEFF4", "#D8DEE9")
    };
    public CatalogFixture ModpacksVm { get; } = new();
    public ICommand ToggleFavoriteCommand => new FixtureCommand(p => { if (p is InstanceModel item) item.IsFavorite = !item.IsFavorite; });
    public int LaunchCount { get; private set; }
    public InstanceModel? LastLaunched { get; private set; }
    public ICommand LaunchSpecificInstanceCommand => new FixtureCommand(p => { LaunchCount++; LastLaunched = p as InstanceModel; });
    public ICommand SaveSettingsCommand => new FixtureCommand(_ => { });
    public ICommand SelectAccountCommand => new FixtureCommand(_ => { });
    public ICommand DeleteAccountCommand => new FixtureCommand(_ => { });
    public ICommand AddAccountCommand => new FixtureCommand(_ => { });
    public SidebarFixture(int count = 0)
    {
        if (count > 0) { Instances.Clear(); for (int i = 0; i < count; i++) Instances.Add(new() { Name = "Instance " + i, Version = "26.3", Loader = "Fabric", IsInstalled = true }); }
        SelectTabCommand = new FixtureCommand(parameter => Select((string)parameter!));
        Library = new(Instances);
        Accounts.Add(ActiveAccount);
        Accounts.Add(new("Alex"));
    }
    public void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Select(string tab)
    {
        ActiveTab = tab;
        foreach (var name in new[] { nameof(ActiveTab), nameof(IsInstancesTab), nameof(IsModpacksTab), nameof(IsAccountsTab), nameof(IsSettingsTab), nameof(PageEyebrow), nameof(PageTitle), nameof(PageDescription) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
sealed class FixtureCommand(Action<object?> run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run(parameter);
}


sealed class CatalogFixture : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ContentTitle { get; set; } = "Mods";
    public string CompatibilityLabel => "Minecraft 26.3 · Fabric";
    public string SearchQuery { get; set; } = "";
    public bool IsInstalling => false;
    public bool IsSearching => false;
    public bool IsLoadingVersions => false;
    public bool HasMore => true;
    public int SelectedCount => 1;
    public string StatusMessage => "Content compatible with your instance.";
    public ModSearchFilter[] Categories { get; } = { new("", "All categories"), new("optimization", "Optimization") };
    public ModSearchFilter[] Loaders { get; } = { new("", "All loaders"), new("fabric", "Fabric") };
    public ModSearchFilter[] SortOptions { get; } = { new("relevance", "Relevance"), new("downloads", "Downloads") };
    public ModSearchFilter SelectedCategory { get; set; }
    public ModSearchFilter SelectedLoader { get; set; }
    public ModSearchFilter SelectedSort { get; set; }
    public ObservableCollection<ModrinthModItemViewModel> SearchResults { get; } = new();
    public ObservableCollection<ModrinthModItemViewModel> Modpacks => SearchResults;
    public ObservableCollection<ModrinthVersionItem> ModpackVersions { get; } = new();
    public ModrinthVersionItem SelectedVersion { get; set; }
    private ModrinthModItemViewModel _selectedMod = null!;
    public ModrinthModItemViewModel SelectedMod
    {
        get => _selectedMod;
        set
        {
            _selectedMod = value;
            PropertyChanged?.Invoke(this, new(nameof(SelectedMod)));
            PropertyChanged?.Invoke(this, new(nameof(SelectedModpack)));
        }
    }
    public ModrinthModItemViewModel SelectedModpack { get => SelectedMod; set => SelectedMod = value; }
    public ICommand SearchModsCommand => new FixtureCommand(_ => { });
    public ICommand SearchModpacksCommand => SearchModsCommand;
    public ICommand InstallSelectedModsCommand => SearchModsCommand;
    public ICommand InstallSelectedModpackCommand => SearchModsCommand;
    public ICommand ResetFiltersCommand => SearchModsCommand;
    public ICommand LoadMoreCommand => SearchModsCommand;
    public ICommand ClearSelectionCommand => SearchModsCommand;
    public CatalogFixture()
    {
        SelectedCategory = Categories[0]; SelectedLoader = Loaders[0]; SelectedSort = SortOptions[0];
        ModpackVersions.Add(new() { Name = "Release 2.0 · Minecraft 26.3", VersionNumber = "2.0" });
        ModpackVersions.Add(new() { Name = "Release 1.9 · Minecraft 26.3", VersionNumber = "1.9" });
        SelectedVersion = ModpackVersions[0];
        foreach (var name in new[] { "A beautiful new adventure", "Performance essentials", "A deliberately long project title that should wrap neatly in a narrow details panel" })
        {
            var item = new ModrinthModItemViewModel(new ModrinthModItem { Title = name, Author = "Community", Description = "A fresh experience with thoughtfully selected content. Explore, build, and make your world your own." });
            item.AvailableVersions.Add(ModpackVersions[0]); item.AvailableVersions.Add(ModpackVersions[1]);
            item.SelectedVersion = ModpackVersions[0]; item.VersionStatus = "Two compatible versions available.";
            SearchResults.Add(item);
        }
        SelectedMod = SearchResults[0]; SelectedMod.IsSelected = true;
    }
}
sealed class CreateFixture
{
    public string InstanceName { get; set; } = "Survival";
    public string SelectedVersion { get; set; } = "26.3";
    public string[] AvailableVersions { get; } = { "26.3", "1.21.11" };
    public LoaderOption[] AvailableLoaders { get; } = { new("Vanilla"), new("Fabric"), new("Forge") };
    public LoaderOption SelectedLoaderOption { get; set; }
    public bool IsLoadingVersions => false;
    public bool CanUseVortexClient => true;
    public bool VortexClientEnabled { get; set; } = true;
    public bool CanCreate => true;
    public string VortexClientDescription => "FPS, Zoom, Armor HUD, Coordinates and a customizable module menu.";
    public string VortexClientStatus => "Vortex Enabled";
    public string ConfigurationSummary => "Minecraft 26.3 · Fabric";
    public CreateFixture() => SelectedLoaderOption = AvailableLoaders[1];
}
