using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexLauncher.Models;

public partial class ModModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public string StateLabel => IsEnabled ? "Enabled" : "Disabled";
    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(StateLabel));
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _filePath = string.Empty;
}
