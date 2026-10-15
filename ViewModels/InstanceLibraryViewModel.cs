using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Models;
using VortexLauncher.Core.Management;

namespace VortexLauncher.ViewModels;

// A live, in-memory view of the library. Filtering never modifies saved instances.
public partial class InstanceLibraryViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<InstanceModel> _source;
    private readonly System.Collections.Generic.HashSet<InstanceModel> _observed = new();
    private int _deferred;
    private bool _loading;
    public bool IsLoading { get => _loading; set { _loading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } }
    public IDisposable DeferRefresh() { _deferred++; return new RefreshScope(this); }
    private sealed class RefreshScope(InstanceLibraryViewModel owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; if (--owner._deferred == 0) { owner.ObserveInstances(); owner.Refresh(); } }
    }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _favoritesOnly;

    public ResettableCollection<InstanceModel> VisibleInstances { get; } = new();
    public bool IsAllSelected => !FavoritesOnly;
    public bool IsEmpty => !IsLoading && VisibleInstances.Count == 0;
    public bool HasInstances => _source.Count > 0;
    public string CountLabel => $"{VisibleInstances.Count} of {_source.Count} instances";
    public string EmptyTitle => HasInstances ? "No matching instances" : "Your next adventure starts here";
    public string EmptyDescription => HasInstances
        ? "Try another name, Minecraft version or loader, or clear your filters."
        : "Create your first instance or import a pack to start playing.";

    public InstanceLibraryViewModel(ObservableCollection<InstanceModel> source)
    {
        _source = source;
        _source.CollectionChanged += OnCollectionChanged;
        ObserveInstances();
        Refresh();
    }

    [RelayCommand] private void ShowAll() => FavoritesOnly = false;
    [RelayCommand] private void ShowFavorites() => FavoritesOnly = true;
    [RelayCommand] private void ClearFilters() { SearchQuery = string.Empty; FavoritesOnly = false; }
    partial void OnSearchQueryChanged(string value) => Refresh();
    partial void OnFavoritesOnlyChanged(bool value) { OnPropertyChanged(nameof(IsAllSelected)); Refresh(); }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferred > 0) return;
        ObserveInstances();
        Refresh();
    }

    private void ObserveInstances()
    {
        var present = _source.ToHashSet();
        foreach (var removed in _observed.Where(item => !present.Contains(item)).ToArray())
        { removed.PropertyChanged -= OnInstanceChanged; _observed.Remove(removed); }
        foreach (var item in _source)
            if (_observed.Add(item)) item.PropertyChanged += OnInstanceChanged;
    }

    private void OnInstanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceModel.Name) or nameof(InstanceModel.Version)
            or nameof(InstanceModel.Loader) or nameof(InstanceModel.IsFavorite)) Refresh();
    }

    private void Refresh()
    {
        if (_deferred > 0) return;
        string query = (SearchQuery ?? string.Empty).Trim();
        var matches = _source.Where(item => (!FavoritesOnly || item.IsFavorite)
            && (query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Version.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Loader.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.IsFavorite).ToArray();
        if (VisibleInstances.Count == 0 || Math.Abs(matches.Length - VisibleInstances.Count) > 32) VisibleInstances.ReplaceAll(matches);
        var matching = matches.ToHashSet();
        for (int i = VisibleInstances.Count - 1; i >= 0; i--)
            if (!matching.Contains(VisibleInstances[i])) VisibleInstances.RemoveAt(i);
        for (int i = 0; i < matches.Length; i++)
        {
            int current = VisibleInstances.IndexOf(matches[i]);
            if (current < 0) VisibleInstances.Insert(i, matches[i]);
            else if (current != i) VisibleInstances.Move(current, i);
        }
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(CountLabel)); OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    public void Dispose()
    {
        _source.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _observed) item.PropertyChanged -= OnInstanceChanged;
        _observed.Clear();
    }
}
