using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Media;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;
using VortexLauncher.Converters;

static class StabilityUiChecks
{
    public static void Run(Action<int> pump, string output)
    {
        var large = new SidebarFixture(500);
        var clock = Stopwatch.StartNew();
        var window = new MainWindow { DataContext = large }; window.Show(); pump(100);
        int realized = window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("InstanceCard"));
        Check(realized is > 0 and < 60, "500 instances realize only a bounded viewport of cards");
        var scroller = window.FindControl<InstanceTileGrid>("instanceCards")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, scroller.Extent.Height); pump(300);
        Check(window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("InstanceCard") && (b.DataContext as InstanceModel)?.Name == "Instance 499"), "virtualized scrolling reaches the final instance");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "virtualized-library.png"));
        large.InterruptedOperations.Add(new() { Kind = "Import", Name = "A very long interrupted modpack name that must fit safely in the recovery banner" });
        large.Notify(nameof(large.HasInterruptedOperations)); window.Width = 960; window.Height = 600; pump(100);
        var resume = window.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Resume");
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Cancel (keep files)");
        Check(resume.Command != null && cancel.Command != null && resume.IsEffectivelyVisible && cancel.IsEffectivelyVisible, "recovery banner exposes explicit resume/cancel commands without automatic replay");
        foreach (var action in new[] { resume, cancel })
        {
            var position = action.TranslatePoint(default, window)!.Value;
            Check(position.X >= 0 && position.X + action.Bounds.Width <= window.ClientSize.Width && position.Y + action.Bounds.Height <= window.ClientSize.Height, "recovery action fits the minimum window");
        }
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "interrupted-recovery.png"));
        window.Close(); large.Library.Dispose();
        Console.WriteLine($"Library fixture: 500 items, {realized} realized cards, {clock.ElapsedMilliseconds} ms including fixed test waits");

        string root = Directory.CreateDirectory(Path.Combine(output, "startup")).FullName;
        var settings = new SettingsManager(Path.Combine(root, "config"));
        settings.SaveSettings(new() { RamAllocationGb = 7 });
        var accounts = new AccountManager(Path.Combine(root, "config")); accounts.SaveAccounts(new() { new("FixturePlayer") { IsActive = true } });
        var instances = new InstanceManager(Path.Combine(root, "instances"));
        for (int i = 0; i < 30; i++) instances.CreateInstance("Pack " + i, "26.3", "Fabric");
        string before = File.ReadAllText(Path.Combine(root, "config/settings.json"));
        var vm = new MainWindowViewModel(instances, settings, accounts, checkUpdates: false);
        Check(vm.IsInitializing && vm.Instances.Count == 0 && File.ReadAllText(Path.Combine(root, "config/settings.json")) == before, "constructing the main view model does not synchronously read/write the library or settings");
        var initialized = vm.InitializeAsync();
        Wait(initialized, pump);
        Check(!vm.IsInitializing && vm.Instances.Count == 30 && vm.RamGb == 7 && vm.ActiveAccount.Username == "FixturePlayer", "background startup applies saved profiles/settings and the complete library");
        Check(ReferenceEquals(initialized, vm.InitializeAsync()), "repeated window initialization shares one startup task");
        Check(vm.StartupTiming.Contains("30 instances"), "startup reports measured library load time");
        vm.Library.SelectedInstance=vm.Instances[10];
        Check(vm.SelectedInstance==vm.Instances[10], "library selection updates the production selected instance");
        vm.Library.SearchQuery="Pack 2";
        vm.SelectedInstance=vm.Instances[10];
        Check(vm.Library.SelectedInstance==vm.Instances[10] && vm.Library.VisibleInstances.Contains(vm.Instances[10]), "explicit instance selection clears conflicting filters and keeps the preview in sync");
        vm.CancelStartup(); vm.Library.Dispose();

        ArtworkCache.DiskRoot = Path.Combine(root, "artwork-cache");
        string imagePath = Path.Combine(root, "cover.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(1800, 1000), new Vector(96, 96))) bitmap.Save(imagePath);
        var imageA = new AsyncImage { SourcePath = imagePath, DecodeWidth = 256 };
        var imageB = new AsyncImage { SourcePath = imagePath, DecodeWidth = 256 };
        var host = new Window { Width = 360, Height = 260, Content = new StackPanel { Children = { imageA, imageB } } }; host.Show();
        var timeout = Stopwatch.StartNew(); while ((imageA.Source == null || imageB.Source == null) && timeout.ElapsedMilliseconds < 10000) pump(20);
        Check(imageA.Source is Bitmap first && first.PixelSize.Width <= 256 && ReferenceEquals(imageA.Source, imageB.Source), "asynchronous artwork shares one downscaled bitmap for matching requests");
        host.Close();
        Check(imageA.Source == null && imageB.Source == null, "detaching images releases their bitmap leases");
        for (int i = 0; i < 80; i++)
        {
            string item = Path.Combine(root, "cover-" + i + ".png"); File.Copy(imagePath, item);
            var task = ArtworkCache.AcquireAsync(item, 544, default); Wait(task, pump); task.Result.Dispose();
        }
        var cache = ArtworkCache.Diagnostics;
        Check(cache.Count <= 64 && cache.Bytes <= 16 * 1024 * 1024, "released artwork entries obey both the entry count and decoded-memory budget");
        Check(Directory.GetFiles(ArtworkCache.DiskRoot, "*.png").Length > 0, "decoded thumbnails are cached on disk for later launches");
        Console.WriteLine("STABILITY_UI_OK");
    }
    private static void Wait(Task task, Action<int> pump)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) pump(10);
        if (!task.IsCompleted) throw new Exception("Fixture timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
