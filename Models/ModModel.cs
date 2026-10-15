using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexLauncher.Models;

public partial class ModModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _filePath = string.Empty;
}