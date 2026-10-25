using System.Diagnostics;
using VortexLauncher.Core.Launch;
using VortexLauncher.ViewModels;

// Manual benchmark, never part of the default run: drives the real MainWindowViewModel
// through "create instance" against Mojang/loader servers, in a throwaway data folder,
// and prints a timeline of the status texts the player would see.
// Usage: dotnet run --project Tests/SidebarSmoke -- --benchmark-create <version> <loader> [client]
internal static class RealCreateBenchmark
{
    internal static void Run(string version, string loader, bool vortexClient, Action<int> pump)
    {
        string root = Directory.CreateDirectory(Path.Combine(FixturePaths.TempRoot(), "vortex-create-benchmark-" + Guid.NewGuid().ToString("N"))).FullName;
        PlatformSupport.DataRootOverride = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var vm = new MainWindowViewModel();
        var clock = Stopwatch.StartNew();
        string last = "";
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainWindowViewModel.StatusMessage) || vm.StatusMessage == last) return;
            last = vm.StatusMessage;
            if (!last.Contains("Preparing Minecraft files (") || last.EndsWith("00...")) Console.WriteLine($"[{clock.Elapsed.TotalSeconds,6:0.00}s] {last}");
        };
        Wait(vm.InitializeAsync(), pump);
        Console.WriteLine($"[{clock.Elapsed.TotalSeconds,6:0.00}s] launcher initialized");
        clock.Restart();
        Wait(vm.AddInstanceAsync("Benchmark", version, loader, vortexClient), pump);
        Console.WriteLine($"CREATE_SECONDS={clock.Elapsed.TotalSeconds:0.00}");
        var instance = vm.Instances.Single();
        foreach (string file in new[] { ".vortex-installation-stages.txt", ".vortex-installation-timing.txt" })
        {
            string path = Path.Combine(instance.InstancePath, file);
            if (File.Exists(path)) Console.WriteLine(File.ReadAllText(path));
        }
        vm.CloseLauncherSessions(); vm.CancelStartup(); vm.Library.Dispose();
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static void Wait(Task task, Action<int> pump)
    {
        while (!task.IsCompleted) pump(20);
        task.GetAwaiter().GetResult();
    }
}
