using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    public ResettableCollection<InterruptedOperation> InterruptedOperations { get; } = new();
    public bool HasInterruptedOperations => InterruptedOperations.Count > 0;
    private void SetRecoveries(IEnumerable<InterruptedOperation?> items)
    {
        InterruptedOperations.ReplaceAll(items.OfType<InterruptedOperation>());
        OnPropertyChanged(nameof(HasInterruptedOperations));
    }
    private async Task RefreshRecoveriesAsync()
    {
        try
        {
        var disk = await Task.Run(() => _instanceManager.LoadInstances());
        using (Library.DeferRefresh())
            foreach (var item in disk) if (!Instances.Any(i => i.InstancePath == item.InstancePath)) Instances.Add(item);
        var instances = Instances.Select(i => (Model: i, i.InstancePath, i.Name, HasMetadata: i.MetadataProblem.Length == 0)).ToArray();
        var items = await Task.Run(() => instances.Select(i => OperationJournal.Inspect(i.InstancePath, i.Name, i.HasMetadata)).ToArray());
        for (int i = 0; i < instances.Length; i++) instances[i].Model.HasInterruptedOperation = items[i] != null;
        SetRecoveries(items);
        }
        catch (Exception ex) { StatusMessage = "Could not refresh saved operations: " + ex.Message; }
    }
    [RelayCommand] private void OpenInterruptedFolder(InterruptedOperation? item)
    {
        if (item == null) return;
        try { PlatformSupport.OpenPath(_instanceManager.ValidateRecoveryTarget(item.Target)); }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
    [RelayCommand] private async Task CancelInterruptedAsync(InterruptedOperation? item)
    {
        if (item == null || IsInitializing || IsGlobalLoading || IsDownloading) return;
        IsGlobalLoading = true; LoadingTitle = "Updating saved operation";
        try
        {
            _instanceManager.ValidateRecoveryTarget(item.Target);
            await Task.Run(() => OperationJournal.Cancel(item));
            await RefreshRecoveriesAsync();
            StatusMessage = "Operation cancelled. All files were kept; Resume remains available.";
        }
        catch (Exception ex) { StatusMessage = "Could not cancel the saved operation: " + ex.Message; }
        finally { IsGlobalLoading = false; }
    }
    [RelayCommand] private async Task ResumeInterruptedAsync(InterruptedOperation? item)
    {
        if (item == null || !item.CanResume || IsInitializing || IsGlobalLoading || IsDownloading) return;
        bool delegatedInstall = false;
        IsGlobalLoading = true; LoadingTitle = "Resuming " + item.Kind;
        try
        {
            _instanceManager.ValidateRecoveryTarget(item.Target);
            var saved = await Task.Run(() => OperationJournal.Read(item.Target));
            if (saved == null || saved.Id != item.Id) throw new InvalidOperationException("The operation changed; reload the library.");
            var instance = Instances.FirstOrDefault(i => i.InstancePath == item.Target);
            if (instance == null || instance.IsRunning || instance.IsInstalling) return;
            if (item.Kind is "Install" or "Repair")
            {
                instance.HasInterruptedOperation = false;
                delegatedInstall = true; IsGlobalLoading = false;
                await InstallInstanceAsync(instance, item.Kind == "Repair");
                return;
            }
            IsGlobalLoading = true; LoadingTitle = "Resuming " + item.Kind;
            using var cancellation = new CancellationTokenSource();
            _installationCancellation = cancellation; CanCancelInstallation = true;
            using var operation = DownloadCenter.Begin("Resume: " + item.Name, cancellation.Cancel);
            try
            {
                var transfer = new TransferProgress(s => { operation.Report(s); UpdateTransferProgress(s); });
                InstanceModel completed;
                if (item.Kind == "Import") completed = await Task.Run(() => _instanceManager.ImportArchiveAsync(item.Source, item.Fingerprint, cancellation.Token, transfer, item.Target));
                else
                {
                    var source = Instances.FirstOrDefault(i => i.InstancePath == item.Source) ?? throw new InvalidOperationException("The copy source is unavailable.");
                    completed = await Task.Run(() => _instanceManager.CloneAsync(source, item.Target, cancellation.Token, transfer));
                }
                completed.IsInstalled = IsInstanceReady(completed);
                int index = Instances.IndexOf(instance); Instances[index] = completed; SelectedInstance = completed;
                operation.Complete(); StatusMessage = "Recovered " + completed.Name;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { operation.Cancelled(); StatusMessage = "Stopped safely. The saved operation can be resumed later."; }
            catch (Exception ex) { operation.Fail(ex); throw; }
        }
        catch (Exception ex) { StatusMessage = "Recovery could not finish: " + ex.Message; }
        finally
        {
            if (!delegatedInstall)
            {
                CanCancelInstallation = false; await RefreshRecoveriesAsync();
                _installationCancellation = null; IsGlobalLoading = false;
            }
        }
    }
}
