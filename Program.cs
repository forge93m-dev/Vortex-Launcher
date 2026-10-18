using System;
using Avalonia;

namespace VortexLauncher;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--launcher-version")
        {
            Console.WriteLine($"{new VortexLauncher.Core.Network.UpdateService().CurrentVersion} {VortexLauncher.Core.Launch.PlatformSupport.RuntimeId}");
            return;
        }
        if (VortexLauncher.Core.Network.LauncherUpdateInstaller.TryRunHelper(args)) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
