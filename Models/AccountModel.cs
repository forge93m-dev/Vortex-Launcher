using System;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexLauncher.Models;

public partial class AccountModel : ObservableObject
{
    [ObservableProperty]
    private string _username = "Steve";

    [ObservableProperty]
    private string _uuid = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private DateTimeOffset? _lastUsedUtc;

    public AccountModel()
    {
    }

    public AccountModel(string username)
    {
        Username = username;
        Uuid = GenerateOfflineUuid(username);
    }

    public static string GenerateOfflineUuid(string username)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"OfflinePlayer:{username}");
        byte[] hash = MD5.HashData(bytes);

        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return new Guid(hash).ToString();
    }
}
