using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VortexLauncher.Core.Management;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    private Task? _startup;
    private readonly CancellationTokenSource _lifetime = new();
    [ObservableProperty] private bool _isInitializing = true;
    [ObservableProperty] private string _startupTiming = "";
    public Task InitializeAsync() => _startup ??= InitializeCoreAsync();
    public void CancelStartup() => _lifetime.Cancel();
    private async Task InitializeCoreAsync()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var settings = _settingsManager.LoadSettings();
                var accounts = _accountManager.LoadAccounts();
                var instances = _instanceManager.LoadInstances(_lifetime.Token);
                foreach (var instance in instances) instance.IsInstalled = IsInstanceReady(instance);
                var operations = instances.Select(OperationJournal.Discover).Where(item => item != null).ToArray();
                return (settings, accounts, instances, operations);
            }, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            ApplySettings(snapshot.settings);
            Accounts.Clear(); foreach (var account in snapshot.accounts) Accounts.Add(account);
            ActiveAccount = Accounts.FirstOrDefault(a => a.IsActive) ?? Accounts.FirstOrDefault() ?? new("Steve");
            if (Accounts.Count == 0) Accounts.Add(ActiveAccount);
            foreach (var account in Accounts) account.IsActive = ReferenceEquals(account, ActiveAccount);
            SortAccounts();
            using (Library.DeferRefresh())
            { Instances.Clear(); foreach (var instance in snapshot.instances) Instances.Add(instance); SortInstances(); }
            SelectedInstance = Instances.FirstOrDefault();
            SetRecoveries(snapshot.operations!);
            StatusMessage = string.Join(" ", new[] { _settingsManager.LoadNotice, _accountManager.LoadNotice }
                .Concat(_instanceManager.LoadNotices).Where(s => !string.IsNullOrWhiteSpace(s)).Take(3));
            if (StatusMessage.Length == 0) StatusMessage = "Ready";
            if (!_settingsManager.HasLoadError) await Task.Run(() => _settingsManager.SaveSettings(snapshot.settings), _lifetime.Token);
            OnPropertyChanged(nameof(UpdateSource));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusMessage = "Library could not be loaded: " + ex.Message; }
        finally
        {
            Library.IsLoading = false; IsInitializing = false;
            StartupTiming = $"Library: {Instances.Count} instances in {clock.ElapsedMilliseconds} ms";
        }
        // Network is not on the critical path to a usable library.
        if (_checkUpdatesOnStartup && !_lifetime.IsCancellationRequested) _ = CheckForUpdatesAsync();
        if (_checkUpdatesOnStartup && !_lifetime.IsCancellationRequested) _ = RestoreVortexSessionAsync();
    }
}
