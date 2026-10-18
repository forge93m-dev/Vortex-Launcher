using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

namespace VortexLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            try {
                _ = Core.Launch.PlatformSupport.DataRoot;
                desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            } catch (System.Exception error) when (error is System.IO.IOException or System.UnauthorizedAccessException or System.ArgumentException) {
                desktop.MainWindow = new TaskDialog("Library unavailable", error.Message + "\n\nVortex did not switch to another data folder or change your instances. Check the installation folder and restart the launcher.");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
