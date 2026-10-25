using Avalonia.Controls.ApplicationLifetimes;
using VortexLauncher.Core.Theme;
using VortexLauncher.Views;

// An interactive preview uses only the same fixture commands as the headless
// tests. It never creates MainWindowViewModel or reads real settings/accounts.
public sealed class GameLibraryPreview : VortexLauncher.App
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ThemeManager.SetTheme("Midnight", "#A78BFA", "#15161C", "#1D1F27", "#252833", "#2E3140", "#ECEDF3", "#A0A4B3");
            var fixture = new SidebarFixture();
            var window = new MainWindow { Title="Vortex Library — preview (test data)", DataContext=fixture };
            window.KeyDown += (_, e) =>
            {
                // Preview-only service windows, never real accounts/instances.
                if (e.Key == Avalonia.Input.Key.F6)
                    new InstanceDetailsWindow(new VortexLauncher.Models.InstanceModel { Name="Survival preview", Version="26.3", Loader="Fabric", IsInstalled=true, InstancePath=System.IO.Path.Combine(FixturePaths.TempRoot(),"vortex-preview-"+System.Guid.NewGuid().ToString("N")) }).Show(window);
                else if (e.Key == Avalonia.Input.Key.F7) new CreateInstanceWindow(new CreateFixture()).Show(window);
                else if (e.Key == Avalonia.Input.Key.F8) new DownloadsWindow().Show(window);
            };
            window.Closed += (_,_) => fixture.Library.Dispose();
            desktop.MainWindow=window;
        }
        // App's implementation creates the production VM; deliberately skip it.
    }
}
