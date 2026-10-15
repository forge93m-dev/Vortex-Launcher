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

    public AccountManager()
    {
        string dir = Path.Combine(PlatformSupport.DataRoot, "config");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "accounts.json");
    }

    public List<AccountModel> LoadAccounts()
    {
        if (!File.Exists(_configPath))
        {
            var defaultAcc = new AccountModel("Steve") { IsActive = true };
            var list = new List<AccountModel> { defaultAcc };
            SaveAccounts(list);
            return list;
        }

        try
        {
            string json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<List<AccountModel>>(json) ?? new List<AccountModel>();
        }
        catch
        {
            return new List<AccountModel> { new AccountModel("Steve") { IsActive = true } };
        }
    }

    public void SaveAccounts(List<AccountModel> accounts)
    {
        string json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
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
