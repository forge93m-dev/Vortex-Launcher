using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class ProfileChecks
{
    public static void Run(Action<int> pump, string output)
    {
        var root = Path.Combine(output, "profiles");
        var manager = new AccountManager(Path.Combine(root, "config"));
        var first = new AccountModel("OldPlayer") { IsActive = true };
        var second = new AccountModel("NextPlayer");
        string legacyUuid = first.Uuid;
        manager.SaveAccounts(new() { first, second });
        var vm = new MainWindowViewModel(new InstanceManager(Path.Combine(root, "instances")), new SettingsManager(Path.Combine(root, "config")), manager, checkUpdates: false);
        var init = vm.InitializeAsync();
        var clock = Stopwatch.StartNew();
        while (!init.IsCompleted && clock.ElapsedMilliseconds < 15000) pump(10);
        init.GetAwaiter().GetResult();
        Check(vm.Accounts.Count == 2 && vm.ActiveAccount.Uuid == legacyUuid && vm.Accounts[0].LastUsedUtc == null, "legacy profiles retain UUIDs and load without timestamps");
        first = vm.Accounts[0]; second = vm.Accounts[1];
        vm.SelectAccountCommand.Execute(second);
        Check(vm.Accounts[0] == second && vm.ActiveAccount == second && second.LastUsedUtc != null && vm.Accounts.Count(a => a.IsActive) == 1, "selection updates recency, order and exactly one active profile");
        var stored = manager.LoadAccounts();
        Check(stored[0].Username == second.Username && stored[0].IsActive && stored[0].LastUsedUtc != null, "recent order and selection survive disk reload");
        vm.NewAccountUsername = " nextplayer "; vm.AddAccountCommand.Execute(null);
        Check(vm.Accounts.Count == 2 && vm.ProfileError.Length > 0, "duplicate names are rejected case-insensitively");
        vm.NewAccountUsername = "Bad name!"; vm.AddAccountCommand.Execute(null);
        Check(vm.Accounts.Count == 2 && vm.ProfileError.Length > 0, "invalid player names are rejected");
        vm.EditAccountCommand.Execute(first); vm.EditedAccountUsername = "Cancelled"; vm.CancelEditAccountCommand.Execute(null);
        Check(first.Username == "OldPlayer", "cancel editing leaves the saved name untouched");
        vm.EditAccountCommand.Execute(first); vm.EditedAccountUsername = first.Username; vm.SaveAccountNameCommand.Execute(null);
        first = vm.Accounts.Single(a => a.Username == "OldPlayer");
        Check(first.Uuid == legacyUuid, "no-op rename preserves legacy UUID");
        vm.EditAccountCommand.Execute(first); vm.EditedAccountUsername = "RenamedPlayer"; vm.SaveAccountNameCommand.Execute(null);
        first = vm.Accounts.Single(a => a.Username == "RenamedPlayer");
        Check(first.Uuid == AccountModel.GenerateOfflineUuid("RenamedPlayer") && vm.ActiveAccount == second, "rename updates offline identity without changing another active profile");
        vm.EditAccountCommand.Execute(second); vm.EditedAccountUsername = "CurrentPlayer"; vm.SaveAccountNameCommand.Execute(null);
        second = vm.ActiveAccount;
        Check(second.Username == "CurrentPlayer" && second.IsActive, "renaming the active player updates the selected launch identity");
        vm.DeleteAccountCommand.Execute(first); vm.CancelDeleteAccountCommand.Execute(null);
        Check(vm.Accounts.Contains(first), "delete requires confirmation and can be cancelled");
        vm.DeleteAccountCommand.Execute(second); vm.ConfirmDeleteAccountCommand.Execute(null);
        Check(vm.Accounts.Count == 1 && vm.ActiveAccount == first && first.IsActive, "deleting active player selects an existing remaining profile");
        vm.DeleteAccountCommand.Execute(first); vm.ConfirmDeleteAccountCommand.Execute(null);
        Check(vm.Accounts.Count == 1 && vm.ProfileError.Length > 0, "last profile cannot be removed");
        vm.CancelDeleteAccountCommand.Execute(null);
        vm.NewAccountUsername = "QuickPlayer"; vm.AddAccountCommand.Execute(null);
        Check(vm.Accounts.Count == 2 && vm.Accounts[0] == vm.ActiveAccount && vm.NewAccountUsername == "", "new player is selected and placed first");

        var host = new MainWindow { DataContext = vm, Width = 960, Height = 600 };
        host.Show(); pump(600);
        var switcher = host.FindControl<Button>("profileSwitcher")!;
        var click = switcher.TranslatePoint(new Point(switcher.Bounds.Width / 2, switcher.Bounds.Height / 2), host)!.Value;
        host.MouseDown(click, Avalonia.Input.MouseButton.Left); host.MouseUp(click, Avalonia.Input.MouseButton.Left); pump(100);
        var flyout = (Flyout)switcher.Flyout!;
        Check(flyout.IsOpen, "clicking the top profile button opens the quick switcher");
        var panel = (Control)flyout.Content!;
        var quick = panel.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.CommandParameter, first));
        Check(quick.IsEffectivelyVisible && quick.Bounds.Width > 100, "top profile flyout contains readable saved player choices");
        quick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(60);
        Check(vm.ActiveAccount == first && vm.ActiveTab == "Instances" && !flyout.IsOpen, "quick selection closes popup without leaving the current tab");
        flyout.ShowAt(switcher); pump(60);
        panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Manage player names").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(80);
        Check(vm.IsAccountsTab && !flyout.IsOpen, "popup management action opens player profiles");
        vm.EditAccountCommand.Execute(first); pump(100);
        var input = host.FindControl<TextBox>("profileNameInput")!;
        Check(input.IsEffectivelyVisible && input.Text == first.Username, "rename editor binds the selected player at minimum window size");
        var save = host.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveAccountNameCommand));
        var savePosition = save.TranslatePoint(default, host)!.Value;
        Check(savePosition.Y + save.Bounds.Height < host.ClientSize.Height - 40, "rename save action fits above the footer at minimum window size");
        var editorScroll = save.GetVisualAncestors().OfType<ScrollViewer>().First();
        var inEditor = save.TranslatePoint(default, editorScroll)!.Value;
        host.CaptureRenderedFrame()!.Save(Path.Combine(output, "profiles-edit-minimum.png"));
        Check(inEditor.Y + save.Bounds.Height <= editorScroll.Viewport.Height + .5, "rename actions are fully visible without scrolling");
        host.CaptureRenderedFrame()!.Save(Path.Combine(output, "profiles-edit-minimum.png"));
        host.Close();

        // Force a write failure with a regular file used as config directory.
        var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "fixture");
        var failed = new MainWindowViewModel(new InstanceManager(Path.Combine(root, "unused")), new SettingsManager(Path.Combine(root, "unused-config")), new AccountManager(blocked), checkUpdates: false) { IsInitializing = false };
        var original = failed.ActiveAccount;
        failed.NewAccountUsername = "UnsavedPlayer"; failed.AddAccountCommand.Execute(null);
        Check(failed.Accounts.Count == 1 && failed.ActiveAccount == original && failed.ProfileError.Length > 0, "failed writes do not publish unsaved profiles or selection");
        failed.CancelStartup(); failed.Library.Dispose();
        Console.WriteLine("PROFILES_OK");
    }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
}
