using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VortexLauncher.Models;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Management;

public class AccountManager
{
    private readonly string _configPath;

    public string LoadNotice { get; private set; } = "";
    public AccountManager(string? configDirectory = null)
    {
        string dir = configDirectory ?? Path.Combine(PlatformSupport.DataRoot, "config");
        _configPath = Path.Combine(dir, "accounts.json");
    }

    public List<AccountModel> LoadAccounts()
    {
        var stored = SafeJsonStore.Read<List<AccountModel>>(_configPath, list => list.All(a => a != null && !string.IsNullOrWhiteSpace(a.Username)));
        LoadNotice = stored.Notice;
        if (stored.Value != null)
        {
            foreach (var account in stored.Value)
                if (!Guid.TryParse(account.Uuid, out _)) account.Uuid = AccountModel.GenerateOfflineUuid(account.Username);
            return stored.Value;
        }
        if (stored.Damaged) return new List<AccountModel> { new("Steve") { IsActive = true } };
        if (!File.Exists(_configPath))
        {
            var defaultAcc = new AccountModel("Steve") { IsActive = true };
            var list = new List<AccountModel> { defaultAcc };
            SaveAccounts(list);
            return list;
        }

        return new List<AccountModel> { new("Steve") { IsActive = true } };
    }

    public void SaveAccounts(List<AccountModel> accounts)
    {
        SafeJsonStore.Write(_configPath, accounts);
    }

    public AccountModel GetActiveAccount(List<AccountModel> accounts)
    {
        var active = accounts.FirstOrDefault(a => a.IsActive);
        if (active == null && accounts.Count > 0)
        {
            active = accounts[0];
            active.IsActive = true;
            SaveAccounts(accounts);
        }
        return active ?? new AccountModel("Steve") { IsActive = true };
    }
}
