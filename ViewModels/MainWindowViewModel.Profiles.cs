using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Models;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private string _profileError = string.Empty;
    [ObservableProperty] private AccountModel? _editingAccount;
    [ObservableProperty] private string _editedAccountUsername = string.Empty;
    [ObservableProperty] private AccountModel? _pendingDeleteAccount;
    public bool IsEditingAccount => EditingAccount != null;
    public bool HasProfileError => ProfileError.Length > 0;
    partial void OnProfileErrorChanged(string value) => OnPropertyChanged(nameof(HasProfileError));
    public bool HasPendingProfileDeletion => PendingDeleteAccount != null;
    partial void OnEditingAccountChanged(AccountModel? value) => OnPropertyChanged(nameof(IsEditingAccount));
    partial void OnPendingDeleteAccountChanged(AccountModel? value) => OnPropertyChanged(nameof(HasPendingProfileDeletion));

    private bool ValidateProfileName(string input, AccountModel? except, out string name)
    {
        name = input.Trim();
        string candidate = name;
        ProfileError = string.Empty;
        if (name.Length is < 3 or > 16 || name.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            ProfileError = "Use 3–16 characters: Latin letters, numbers or underscore.";
        else if (Accounts.Any(a => !ReferenceEquals(a, except) && a.Username.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            ProfileError = "This player name is already saved.";
        return ProfileError.Length == 0;
    }

    private void SortAccounts()
    {
        // Stable ordering keeps legacy profiles (without timestamps) in their saved order.
        var ordered = Accounts.OrderByDescending(a => a.LastUsedUtc).ToArray();
        for (int i = 0; i < ordered.Length; i++) Accounts.Move(Accounts.IndexOf(ordered[i]), i);
    }

    private bool CommitProfiles(List<AccountModel> profiles, AccountModel active)
    {
        try
        {
            // Save before changing selection/list so failed writes leave the UI intact.
            _accountManager.SaveAccounts(profiles.OrderByDescending(a => a.LastUsedUtc).Select(a => new AccountModel
            { Username = a.Username, Uuid = a.Uuid, LastUsedUtc = a.LastUsedUtc, IsActive = ReferenceEquals(a, active) }).ToList());
            foreach (var old in Accounts.Where(a => !profiles.Contains(a)).ToArray()) Accounts.Remove(old);
            foreach (var item in profiles)
            {
                item.IsActive = ReferenceEquals(item, active);
                if (!Accounts.Contains(item)) Accounts.Add(item);
            }
            ActiveAccount = active;
            SortAccounts();
            ProfileError = string.Empty;
            StatusMessage = "Player profiles saved. Changes apply to the next launch.";
            return true;
        }
        catch (Exception ex)
        {
            ProfileError = "Profiles were not saved: " + ex.Message;
            StatusMessage = ProfileError;
            return false;
        }
    }

    [RelayCommand] private void EditAccount(AccountModel? account)
    {
        if (IsInitializing || account == null || !Accounts.Contains(account)) return;
        PendingDeleteAccount = null;
        EditingAccount = account;
        EditedAccountUsername = account.Username;
        ProfileError = string.Empty;
    }
    [RelayCommand] private void CancelEditAccount()
    {
        EditingAccount = null; EditedAccountUsername = string.Empty; ProfileError = string.Empty;
    }
    [RelayCommand] private void SaveAccountName()
    {
        if (IsInitializing || EditingAccount is not { } old || !Accounts.Contains(old) || !ValidateProfileName(EditedAccountUsername, old, out var name)) return;
        // Keep legacy UUIDs on no-op saves. A genuinely different offline name has a different identity.
        var replacement = new AccountModel { Username = name, Uuid = name == old.Username ? old.Uuid : AccountModel.GenerateOfflineUuid(name), LastUsedUtc = old.LastUsedUtc };
        var list = Accounts.Select(a => ReferenceEquals(a, old) ? replacement : a).ToList();
        if (CommitProfiles(list, ReferenceEquals(ActiveAccount, old) ? replacement : ActiveAccount)) CancelEditAccount();
    }
    [RelayCommand] private void CancelDeleteAccount() { PendingDeleteAccount = null; ProfileError = string.Empty; }
    [RelayCommand] private void ConfirmDeleteAccount()
    {
        if (IsInitializing || PendingDeleteAccount is not { } old || !Accounts.Contains(old)) return;
        var list = Accounts.Where(a => !ReferenceEquals(a, old)).ToList();
        if (list.Count == 0) { ProfileError = "Keep at least one player profile. Add another name first."; return; }
        var active = ReferenceEquals(old, ActiveAccount) ? list.OrderByDescending(a => a.LastUsedUtc).First() : ActiveAccount;
        if (CommitProfiles(list, active)) PendingDeleteAccount = null;
    }
}
