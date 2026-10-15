using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexLauncher.Models;

public partial class InstanceModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _loader = "Vanilla";

    [ObservableProperty]
    private string _loaderVersion = string.Empty;

    [ObservableProperty]
    private bool _vortexClientEnabled;

    [ObservableProperty]
    private string _instancePath = string.Empty;

    [ObservableProperty]
    private string _coverImagePath = string.Empty;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private long _totalPlayTimeSeconds;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isRunning;

    [System.Text.Json.Serialization.JsonIgnore]
    public string FavoriteSymbol => IsFavorite ? "★" : "☆";

    [System.Text.Json.Serialization.JsonIgnore]
    public string LaunchButtonText => IsRunning ? "Running" : IsInstalled ? "Play" : "Resume";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusText => IsBeingDeleted ? "Removing…" : IsInstalling ? "Installing…"
        : IsRunning ? "In game" : IsInstalled ? "Ready to play" : "Not installed";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanChangeFiles => !IsInstalling && !IsRunning && !IsBeingDeleted;

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteSymbol));

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanManageMods));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(CanChangeFiles));
    }

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isInstalled;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isInstalling;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isBeingDeleted;

    [System.Text.Json.Serialization.JsonIgnore]
    public double CardOpacity => IsBeingDeleted ? 0 : 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanManageMods => IsInstalled && !IsInstalling && !IsRunning;

    public string SummaryText => $"{Version} ({Loader})";
    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(SummaryText));
    partial void OnLoaderChanged(string value) => OnPropertyChanged(nameof(SummaryText));
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverImagePath);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);
    partial void OnCoverImagePathChanged(string value) => OnPropertyChanged(nameof(HasCover));
    partial void OnIconPathChanged(string value) => OnPropertyChanged(nameof(HasIcon));

    [System.Text.Json.Serialization.JsonIgnore]
    public string PlayTimeText
    {
        get
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, TotalPlayTimeSeconds));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}h {time.Minutes:D2}m played"
                : $"{time.Minutes}m {time.Seconds:D2}s played";
        }
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanManageMods));
        OnPropertyChanged(nameof(LaunchButtonText));
    }

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanManageMods));
        OnPropertyChanged(nameof(CanChangeFiles));
    }

    partial void OnIsBeingDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(CanChangeFiles));
    }

    partial void OnTotalPlayTimeSecondsChanged(long value)
    {
        OnPropertyChanged(nameof(PlayTimeText));
    }
}
