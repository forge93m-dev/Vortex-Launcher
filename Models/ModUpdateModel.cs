using System;
using CommunityToolkit.Mvvm.ComponentModel;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Models;

public partial class ModUpdateModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    public string SourcePath { get; init; } = "";
    public string OriginalHash { get; init; } = "";
    public ModrinthVersionItem Current { get; init; } = new();
    public ModrinthVersionItem Latest { get; init; } = new();
    public ModrinthFileItem File { get; init; } = new();
    public string Name => System.IO.Path.GetFileName(SourcePath);
    public string VersionChange => $"{Current.DisplayName} → {Latest.DisplayName}";
    public string Details => $"{File.Filename} · {File.Size / 1048576d:0.0} MB · {Latest.DatePublished:yyyy-MM-dd}" +
        (Core.Management.ModFiles.IsDisabled(SourcePath) ? " · stays disabled" : "");
    public string Changelog => string.IsNullOrWhiteSpace(Latest.Changelog) ? "No changelog supplied." : Latest.Changelog[..Math.Min(Latest.Changelog.Length, 16000)];
    public string ProjectUrl => "https://modrinth.com/mod/" + Uri.EscapeDataString(Current.ProjectId);
}
