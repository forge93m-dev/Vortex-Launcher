using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.VisualTree;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class UnifiedWorkspaceChecks
{
    internal static void Run(MainWindow main, SidebarFixture fixture, Action<int> pump, string output)
    {
        using var f = new ModFixture(output); f.Add("alpha"); f.Add("bravo", disabled: true);
        using var fixtureIcon = new RenderTargetBitmap(new PixelSize(24, 24), new Vector(96, 96));
        var iconTile = new Border { Width = 24, Height = 24, Background = Avalonia.Media.Brushes.CornflowerBlue }; iconTile.Measure(new Size(24, 24)); iconTile.Arrange(new Rect(0, 0, 24, 24)); fixtureIcon.Render(iconTile);
        string pack = Path.Combine(f.Root, "resourcepacks", "example.zip"); Directory.CreateDirectory(Path.GetDirectoryName(pack)!); File.WriteAllText(pack, "pack fixture");
        Directory.CreateDirectory(Path.Combine(f.Root, "saves", "My world"));
        var item = new InstanceModel { Name = "Survival with friends", Version = "26.3", Loader = "Fabric", InstancePath = f.Root, IsInstalled = true };
        var catalogues = new Dictionary<string, DownloadModsViewModel>();
        using var workspace = new InstanceWorkspaceView(item, catalogFactory: type => {
            var api = new CatalogApi(type); f.Handler.Files[api.File.Url] = api.Bytes;
            var vm = new DownloadModsViewModel(item.Version, item.Loader, item.InstancePath, type, api, autoSearch: false, downloadClient: f.Http, downloadCacheRoot: Path.Combine(f.Root, "fixture-cache")); catalogues[type] = vm; return vm;
        }, modUpdates: f.Service);
        var host = new Window { Width = 960, Height = 480, Content = workspace, Background = Avalonia.Media.Brushes.Black }; host.Resources["LibraryPlayBrush"] = Avalonia.Media.Brushes.LightGreen; host.Show(); pump(220);
        int launches = 0; workspace.LaunchAsync = () => { launches++; return Task.CompletedTask; }; workspace.RefreshAvailability();
        Click(host, workspace.FindControl<Button>("WorkspacePlay")!); Check(launches == 1, "workspace Play launches the selected instance");
        foreach (var size in new[] { new Size(960, 446), new Size(1280, 646) }) {
            host.Width = size.Width; host.Height = size.Height;
            foreach (string section in new[] { "Overview", "Mods", "Resource packs", "Shader packs", "Worlds", "More files", "Settings" }) {
                workspace.SelectSection(section); pump(220);
                Check(workspace.SelectedSection == section && host.OwnedWindows.Count() == 0, section + " opens inside the same workspace");
                foreach (var button in workspace.FindControl<StackPanel>("SectionTabs")!.Children.OfType<Button>()) Fits(host, button, "workspace tab " + button.Content);
                var selected = workspace.FindControl<StackPanel>("SectionTabs")!.Children.OfType<Button>().Count(b => b.Classes.Contains("active"));
                Check(selected == 1, "one selected workspace tab");
                if (section == "Mods") Check(workspace.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ModRow")).All(b => b.Bounds.Height <= 60), "installed mod rows stay compact");
                if (section == "Mods" && size.Width == 960) {
                    double Rows() => workspace.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ModRow")).Max(b => b.Bounds.Height);
                    double comfortable = Rows();
                    VortexLauncher.Core.Theme.ThemeManager.SetDensity(true); pump(60);
                    double compact = Rows();
                    VortexLauncher.Core.Theme.ThemeManager.SetDensity(false); pump(60);
                    Check(compact < comfortable && Rows() == comfortable, "compact density makes mod rows shorter and comfortable density restores them");
                }
                if(section=="Overview"){
                    var clientAction=workspace.FindControl<Button>("OverviewClientButton")!;
                    Check(clientAction.IsEffectivelyVisible && clientAction.Command!=null,"client install/update is discoverable without opening Settings or More");
                    var summary=workspace.Content;
                    var modsTab=workspace.FindControl<StackPanel>("SectionTabs")!.Children.OfType<Button>().Single(t=>Equals(t.Tag,"Mods"));
                    var badge=((Grid)modsTab.Content!).Children.OfType<TextBlock>().Last().Text;
                    Check(badge==(summary.Mods>0?summary.Mods.ToString():""),"the Mods tab shows how many mods the instance holds");
                    Check(workspace.FindControl<StackPanel>("LaunchRows")!.Children.Count>=1 && workspace.FindControl<StackPanel>("ModRows")!.Children.Count>=1 && workspace.FindControl<StackPanel>("WorldRows")!.Children.Count>=1,
                        "the overview cards for launch settings, mods and worlds are filled in");
                }
                host.CaptureRenderedFrame()!.Save(Path.Combine(output, $"unified-{section.Replace(' ', '-')}-{(int)size.Width}.png"));
            }
            foreach (string type in new[] { "mod", "resourcepack", "shader" }) {
                workspace.SelectSection(type == "mod" ? "Mods" : type == "resourcepack" ? "Resource packs" : "Shader packs"); workspace.OpenCatalog(type);
                var catalog = workspace.CurrentCatalog!; Finish(catalog.SearchModsAsync()); pump(220);
                catalog.ClearSelectionCommand.Execute(null);
                Check(catalog.SearchResults.Count == 6 && !catalog.CanInstallSelection, "inline catalog loads fixture results and requires selection: " + type);
                catalog.SelectedMod = catalog.SearchResults[0]; catalog.SelectedMod.IsSelected = true; pump(180);
                var view = workspace.FindControl<ContentControl>("CatalogHost")!.Content as ContentCatalogView;
                Check(!catalog.SelectedMod.HasIcon, "catalog has an icon fallback before the image loads");
                catalog.SelectedMod.IconBitmap = fixtureIcon; pump(40);
                Check(catalog.SelectedMod.HasIcon && view!.GetVisualDescendants().OfType<Image>().Count(i => (i.Name == "CatalogItemIcon" || i.Name == "CatalogDetailIcon") && ReferenceEquals(i.Source, fixtureIcon)) >= 2, "catalog icons appear in both results and selected details: " + type);
                var detailsPanel = view!.FindControl<Border>("CatalogDetails")!;
                Check(detailsPanel.Bounds.Width >= (size.Width >= 1280 ? 380 : 320) && detailsPanel.Bounds.Width <= 541, "details panel scales with window width instead of staying at 280 pixels");
                if (type == "mod" && size.Width == 960) {
                    var split = view!.GetVisualDescendants().OfType<GridSplitter>().Single(); double before = detailsPanel.Bounds.Width;
                    var point = split.TranslatePoint(new Point(split.Bounds.Width / 2, split.Bounds.Height / 2), host)!.Value;
                    host.MouseMove(point); host.MouseDown(point, MouseButton.Left); host.MouseMove(new Point(point.X - 30, point.Y)); host.MouseUp(new Point(point.X - 30, point.Y), MouseButton.Left); pump(50);
                    Check(detailsPanel.Bounds.Width >= before + 20, "details divider can be dragged to widen the panel");
                }
                CheckPlayCentered(workspace);
                Fits(host, view!.FindControl<Button>("CatalogInstall")!, "inline installation footer");
                host.CaptureRenderedFrame()!.Save(Path.Combine(output, $"unified-catalog-{type}-{(int)size.Width}.png"));
                Check(view!.FindControl<ListBox>("CatalogResults")!.Bounds.Height >= 80, "catalog retains a usable results viewport at minimum height");
                Check(catalog.HasSelectedMod && catalog.SelectedMod.AvailableVersions.Count == 1 && catalog.CanInstallSelection, "compatible version picker and selection state remain active");
                host.CaptureRenderedFrame()!.Save(Path.Combine(output, $"unified-catalog-{type}-{(int)size.Width}.png"));
                item.IsRunning = true; pump(30);
                Check(!catalog.CanInstallSelection && !workspace.Mods.CanMutate, "running game blocks writes across embedded views"); item.IsRunning = false;
                catalog.IsInstalling = true; pump(25);
                Check(workspace.IsBusy && !workspace.FindControl<Button>("ReturnFromCatalog")!.IsEnabled && !workspace.FindControl<Button>("WorkspacePlay")!.IsEnabled, "active installation prevents navigation and game launch races");
                workspace.SelectSection("Worlds"); Check(workspace.SelectedSection != "Worlds", "in-flight install cannot be disposed by switching sections");
                catalog.IsInstalling = false; pump(20); workspace.SelectSection("Mods");
                catalog.SelectedMod.IconBitmap = null;
            }
        }
        foreach (string type in new[] { "mod", "resourcepack", "shader" }) {
            workspace.SelectSection(type == "mod" ? "Mods" : type == "resourcepack" ? "Resource packs" : "Shader packs"); workspace.OpenCatalog(type);
            var vm = workspace.CurrentCatalog!; vm.ClearSelectionCommand.Execute(null); vm.SearchResults[0].IsSelected = true;
            Finish(vm.InstallSelectedModsCommand.ExecuteAsync(null));
            string file = Path.Combine(f.Root, type == "mod" ? "mods" : type == "resourcepack" ? "resourcepacks" : "shaderpacks", type + (type == "mod" ? ".jar" : ".zip"));
            Check(File.Exists(file) && !workspace.IsBusy && vm.StatusMessage.StartsWith("Installed"), "inline catalog installs " + type + " through fixture HTTP without extra windows");
        }
        // Keep the following removal assertions focused on the original two mods.
        File.Delete(Path.Combine(f.Root, "mods", "mod.jar"));
        File.Delete(Path.Combine(f.Root, "resourcepacks", "resourcepack.zip"));
        workspace.SelectSection("Mods"); workspace.Mods.ConfirmRemovalAsync = _ => Task.FromResult(true);
        Finish(workspace.Mods.RemoveModCommand.ExecuteAsync(workspace.Mods.InstalledMods.First()));
        Check(workspace.Mods.InstalledMods.Count == 1 && workspace.Mods.HasUndoRemoval, "embedded mod removal uses recoverable history"); workspace.Mods.UndoRemovalCommand.Execute(null);
        Check(workspace.Mods.InstalledMods.Count == 2, "embedded mod Undo restores the same list");
        workspace.SelectSection("Resource packs"); workspace.Details.ConfirmRemovalAsync = _ => Task.FromResult(true); Finish(workspace.Details.RemoveItemCommand.ExecuteAsync(workspace.Details.Items.Single()));
        Check(!File.Exists(pack) && workspace.Details.HasUndoRemoval, "embedded resource pack removal works"); workspace.Details.UndoRemovalCommand.Execute(null); Check(File.Exists(pack), "embedded resource pack Undo works");
        string imported = Path.Combine(output, "imported.jar"); File.WriteAllBytes(imported, ModFixture.Jar("{}")); workspace.SelectSection("Mods"); Finish(workspace.ImportModFilesAsync(new[] { imported }));
        Check(File.Exists(Path.Combine(f.Root, "mods", "imported.jar")) && workspace.Mods.InstalledMods.Count == 3, "local mod import refreshes the single installed list");
        item.IsRunning = true; string blocked = Path.Combine(output, "blocked.jar"); File.WriteAllBytes(blocked, ModFixture.Jar("{}")); Finish(workspace.ImportModFilesAsync(new[] { blocked })); Check(!File.Exists(Path.Combine(f.Root, "mods", "blocked.jar")), "local import respects running instance guard"); item.IsRunning = false;
        workspace.SelectSection("Settings"); int reports = 0; workspace.Details.ExportReportAsync = () => { reports++; return Task.FromResult("Fixture report"); };
        var report = workspace.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save error report"));
        Check(report.Command != null, "error-report tool has a real command binding"); Finish(workspace.Details.SaveErrorReportCommand.ExecuteAsync(null)); Check(reports == 1, "error-report tool keeps its callback");
        workspace.Mods.SearchQuery = "alpha"; workspace.SelectSection("Worlds"); workspace.SelectSection("Mods"); Check(workspace.Mods.SearchQuery == "alpha", "installed mod search survives section changes");
        workspace.SelectSection("More files"); workspace.FindControl<ComboBox>("ExtraSections")!.SelectedItem = "Servers"; pump(50);
        Check(workspace.Details.IsServersSection && workspace.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, "Save server")), "servers remain available without another window");
        host.Close(); workspace.Dispose(); Check(workspace.IsDisposed, "workspace releases subscriptions and cached catalogues on disposal");
        fixture.Select("Instances"); main.OpenInstanceWorkspace(item, "Mods"); pump(180);
        main.ActiveWorkspace!.LaunchAsync = () => Task.CompletedTask; main.ActiveWorkspace.RefreshAvailability();
        Check(main.ActiveWorkspace?.SelectedSection == "Mods" && !main.FindControl<Grid>("LibraryAndPages")!.IsVisible && main.OwnedWindows.Count() == 0, "main launcher opens selected instance in its content area, not a manager window");
        foreach (var size in new[] { new Size(960, 600), new Size(1280, 800) }) {
            main.Width = size.Width; main.Height = size.Height; pump(160);
            main.CaptureRenderedFrame()!.Save(Path.Combine(output, $"unified-main-mods-{(int)size.Width}.png"));
            Fits(main, main.ActiveWorkspace!.FindControl<Button>("WorkspacePlay")!, "main workspace Play");
            CheckPlayCentered(main.ActiveWorkspace!);
        }
        item.IsInstalling = true; pump(40);
        Check(!main.FindControl<Button>("libraryNav")!.IsEnabled, "main navigation prevents switching away during file operations");
        item.IsInstalling = false; pump(40); Check(main.FindControl<Button>("libraryNav")!.IsEnabled, "main navigation returns immediately after an operation finishes");
        var old = main.ActiveWorkspace; main.HideInstanceWorkspace(); main.OpenInstanceWorkspace(item); Check(ReferenceEquals(old, main.ActiveWorkspace) && main.ActiveWorkspace!.SelectedSection == "Mods", "reopening a workspace remembers its section");
        main.HideInstanceWorkspace(); pump(180);
        Check(main.FindControl<Grid>("LibraryAndPages")!.IsVisible, "return to library preserves the main shell");
        var previousSelection = fixture.Library.SelectedInstance; fixture.Instances.Add(item); fixture.Library.SelectedInstance = item; pump(180);
        var shortcut = main.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Resource packs") && Equals(b.Tag, item));
        Click(main, shortcut);
        Check(main.ActiveWorkspace?.SelectedSection == "Resource packs" && ReferenceEquals(main.ActiveWorkspace.Instance, item), "library shortcut opens the correct instance and section in one click");
        main.HideInstanceWorkspace(); fixture.Instances.Remove(item); fixture.Library.SelectedInstance = previousSelection; pump(60);
        Console.WriteLine("UNIFIED_WORKSPACE_OK");
        void Finish(Task task) { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 12) pump(20); if (!task.IsCompleted) throw new TimeoutException("workspace fixture"); task.GetAwaiter().GetResult(); }
        void Click(Window owner, Button b) { var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), owner)!.Value; owner.MouseMove(p); owner.MouseDown(p, MouseButton.Left); owner.MouseUp(p, MouseButton.Left); pump(80); }
    }
    private static void Fits(Window host, Control c, string text) { var p = c.TranslatePoint(default, host)!.Value; Check(p.X >= 0 && p.Y >= 0 && p.X + c.Bounds.Width <= host.ClientSize.Width + 1 && p.Y + c.Bounds.Height <= host.ClientSize.Height + 1, text + " fits"); }
    private static void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
    private static void CheckPlayCentered(InstanceWorkspaceView workspace)
    {
        var button = workspace.FindControl<Button>("WorkspacePlay")!;
        var label = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == workspace.Instance.LaunchButtonText);
        var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), button)!.Value;
        Check(Math.Abs(center.X - button.Bounds.Width / 2) <= 1 && Math.Abs(center.Y - button.Bounds.Height / 2) <= 1, "workspace Play label is centered on both axes");
    }
    private sealed class CatalogApi : ModrinthService
    {
        public byte[] Bytes { get; }
        public ModrinthFileItem File { get; }
        public CatalogApi(string type)
        {
            using var memory = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true)) {
                using var writer = new StreamWriter(zip.CreateEntry(type == "mod" ? "fabric.mod.json" : type == "resourcepack" ? "pack.mcmeta" : "shaders/test.glsl").Open()); writer.Write("{}");
            }
            Bytes = memory.ToArray();
            File = new ModrinthFileItem { Filename = type + (type == "mod" ? ".jar" : ".zip"), Url = "https://fixture.invalid/" + type, Size = Bytes.Length, Hashes = new() { ["sha1"] = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Bytes)) } };
        }
        public override Task<ModrinthSearchResult> SearchModsAsync(string query, string gameVersion, string loader, int offset = 0, int limit = 20, string projectType = "mod", string? category = null, string sort = "relevance", CancellationToken cancellationToken = default)
            => Task.FromResult(new ModrinthSearchResult { Hits = Enumerable.Range(0, 6).Select(i => new ModrinthModItem { ProjectId = "fixture-" + i, Title = "Fixture content " + i, Author = "Fixture", Description = "Compatible content used only in an offline UI fixture." }).ToList(), TotalHits = 6 });
        public override Task<List<ModrinthVersionItem>> GetProjectVersionsAsync(string projectId) => Task.FromResult(new List<ModrinthVersionItem> { new() { Id = "fixture-version", ProjectId = projectId, Name = "Fixture release", VersionNumber = "1.0", GameVersions = new() { "26.3" }, Loaders = new() { "fabric" }, Files = new() { File } } });
    }
}
