using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Models;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class InstanceModsViewModel : ObservableObject
{
    private readonly string _gameVersion;
    private readonly string _loader;
    private readonly string _instancePath;
    private readonly string _modsDir;
    public Func<bool>? CanChangeFiles { get; set; }
    public Func<string, Task<bool>>? ConfirmRemovalAsync { get; set; }
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ModModel? _selectedMod;

    public ObservableCollection<ModModel> InstalledMods { get; } = new();
    public bool IsEmpty => InstalledMods.Count == 0;

    public InstanceModsViewModel(string gameVersion, string loader, string instancePath)
    {
        InstalledMods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _gameVersion = gameVersion;
        _loader = loader;
        _instancePath = instancePath;
        _modsDir = Path.Combine(_instancePath, "mods");

        Directory.CreateDirectory(_modsDir);
        RefreshInstalledMods();
    }

    public void RefreshInstalledMods()
    {
        InstalledMods.Clear();

        if (!Directory.Exists(_modsDir)) return;

        var files = Directory.GetFiles(_modsDir, "*.*", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            string ext = Path.GetExtension(file).ToLower();
            if (ext == ".jar" || ext == ".disabled")
            {
                bool isEnabled = ext == ".jar";
                string rawFileName = Path.GetFileName(file);
                string cleanFileName = Uri.UnescapeDataString(rawFileName); // Декодирует %2B в +

                // Форматируем понятное имя (берем только название мода до первого дефиса с версией)
                string friendlyName = GetFriendlyModName(cleanFileName);

                InstalledMods.Add(new ModModel
                {
                    Name = friendlyName,
                    FileName = cleanFileName,
                    IsEnabled = isEnabled,
                    FilePath = file
                });
            }
        }
    }

    private static string GetFriendlyModName(string fileName)
    {
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (nameWithoutExt.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            nameWithoutExt = Path.GetFileNameWithoutExtension(nameWithoutExt);
        }

        // Обрезаем номера версий и спецсимволы
        int dashIndex = nameWithoutExt.IndexOf('-');
        if (dashIndex > 0 && !char.IsDigit(nameWithoutExt[0]))
        {
            string clean = nameWithoutExt.Substring(0, dashIndex).Replace('_', ' ');
            if (clean.Length > 1)
            {
                return char.ToUpper(clean[0]) + clean.Substring(1);
            }
        }

        return nameWithoutExt.Replace('-', ' ').Replace('_', ' ');
    }

    [RelayCommand]
    private async Task OpenDownloadModsAsync()
    {
        var downloadModsVm = new DownloadModsViewModel(_gameVersion, _loader, _instancePath);
        var window = new DownloadModsWindow
        {
            DataContext = downloadModsVm
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await window.ShowDialog(desktop.MainWindow);
            RefreshInstalledMods();
        }
    }

    [RelayCommand]
    private void ToggleMod(ModModel? mod)
    {
        if (mod == null || CanChangeFiles?.Invoke() == false || RemoveModCommand.IsRunning || !File.Exists(mod.FilePath)) return;

        string dir = Path.GetDirectoryName(mod.FilePath)!;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(mod.FilePath);

        if (mod.IsEnabled)
        {
            string newPath = Path.Combine(dir, fileNameWithoutExt + ".disabled");
            File.Move(mod.FilePath, newPath);
        }
        else
        {
            string newPath = Path.Combine(dir, fileNameWithoutExt + ".jar");
            File.Move(mod.FilePath, newPath);
        }

        RefreshInstalledMods();
    }

    [RelayCommand]
    private async Task RemoveModAsync(ModModel? mod)
    {
        if (mod == null || CanChangeFiles?.Invoke() == false || !File.Exists(mod.FilePath)) return;

        if (!InstalledMods.Contains(mod) || ConfirmRemovalAsync == null || !await ConfirmRemovalAsync(mod.FileName)) return;
        if (CanChangeFiles?.Invoke() == false || !InstalledMods.Contains(mod)) return;
        try
        {
            Core.Management.InstanceContentRemoval.MoveToBackup(_instancePath, "Mods", mod.FilePath);
            RefreshInstalledMods();
            StatusMessage = "Removed. Backup saved in .vortex-trash.";
        }
        catch (Exception ex) { StatusMessage = $"Could not remove file: {ex.Message}"; }
    }
    [RelayCommand]
    private void OpenModsFolder()
    {
        if (Directory.Exists(_modsDir))
        {
            VortexLauncher.Core.Launch.PlatformSupport.OpenPath(_modsDir);
        }
    }
}
