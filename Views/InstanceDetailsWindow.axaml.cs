using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System.Linq;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

namespace VortexLauncher.Views;

public partial class InstanceDetailsWindow : Window
{
    private CancellationTokenSource? _sectionMotion;
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal : WindowState.Maximized;

    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else BeginMoveDrag(e);
        e.Handled = true;
    }
    private InstanceDetailsViewModel? _observedViewModel;
    private void OnDownloadsClick(object? sender, RoutedEventArgs e) => new DownloadsWindow().Show(this);
    public InstanceDetailsWindow()
        : this(new InstanceModel())
    {
    }

    public InstanceDetailsWindow(InstanceModel instance)
        : this(instance, null)
    {
    }

    public InstanceDetailsWindow(InstanceModel instance, Func<string, Task>? joinServerCallback)
    {
        InitializeComponent();
        sectionContent.AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        sectionContent.AddHandler(DragDrop.DragLeaveEvent, (_, _) => dropHint.IsVisible = false);
        sectionContent.AddHandler(DragDrop.DropEvent, OnFilesDropped);
        DataContextChanged += (_, _) => ObserveSections();
        Closed += (_, _) =>
        {
            if (_observedViewModel != null)
            {
                _observedViewModel.PropertyChanged -= OnSectionChanged;
                _observedViewModel.Dispose();
            }
            _sectionMotion?.Cancel();
        };
        DataContext = new InstanceDetailsViewModel(instance, joinServerCallback);
        if (DataContext is InstanceDetailsViewModel model)
        {
            model.OpenModsManagerAsync = async updates =>
            {
                using var vm = new InstanceModsViewModel(instance.Version, instance.Loader, instance.InstancePath)
                { CanChangeFiles = () => instance.CanChangeFiles, IsPreviewVisible = updates };
                var mods = new InstanceModsWindow { DataContext = vm };
                if (updates) mods.Opened += async (_, _) => await vm.CheckUpdatesCommand.ExecuteAsync(null);
                await mods.ShowDialog(this);
            };
            model.ExportReportAsync = () => DiagnosticReportExport.SaveAsync(this, instance.InstancePath);
        }
    }

    private void ObserveSections()
    {
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged -= OnSectionChanged;
            _observedViewModel.ConfirmRemovalAsync = null;
        }
        _observedViewModel = DataContext as InstanceDetailsViewModel;
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged += OnSectionChanged;
            _observedViewModel.ConfirmRemovalAsync = name => new RemoveContentWindow(name).ShowDialog<bool>(this);
        }
        UpdateSectionNavigation();
    }

    private void UpdateSectionNavigation()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("SideButton")))
        {
            bool selected = Equals(button.Content, _observedViewModel?.SelectedSection);
            if (selected) button.Classes.Add("active");
            else button.Classes.Remove("active");
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        bool accepted = DataContext is InstanceDetailsViewModel vm && vm.CanImportFiles &&
            paths != null && paths.Any(vm.AcceptsFile);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        dropHint.IsVisible = accepted;
        e.Handled = true;
    }

    private async void OnFilesDropped(object? sender, DragEventArgs e)
    {
        dropHint.IsVisible = false;
        e.Handled = true;
        if (DataContext is not InstanceDetailsViewModel vm) return;
        var paths = e.Data.GetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths != null) await vm.ImportFilesAsync(paths);
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceDetailsViewModel vm || !vm.CanImportFiles) return;
        string extension = vm.IsModsSection ? "*.jar" : "*.zip";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(vm.IsModsSection ? "Minecraft mods" : "Minecraft packs") { Patterns = new[] { extension } } }
        });
        await vm.ImportFilesAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InstanceDetailsViewModel.SelectedSection)) return;
        dropHint.IsVisible = false;
        _sectionMotion?.Cancel();
        var cancellation = new CancellationTokenSource();
        _sectionMotion = cancellation;
        try
        {
            // Let the bindings replace the content before revealing it.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            UpdateSectionNavigation();
            await UiMotion.RevealAsync(sectionContent, cancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_sectionMotion, cancellation)) _sectionMotion = null;
            cancellation.Dispose();
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        UpdateSectionNavigation();
        detailsShell.Opacity = 0;
        detailsShell.RenderTransform = new TranslateTransform(0, 12);
        Dispatcher.UIThread.Post(() =>
        {
            detailsShell.Opacity = 1;
            detailsShell.RenderTransform = new TranslateTransform(0, 0);
        }, DispatcherPriority.Render);
    }
}
