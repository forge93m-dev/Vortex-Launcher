using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace VortexLauncher.Views;

// ItemsRepeater keeps a large library virtualized; a regular WrapPanel would
// construct every artwork control, even for hundreds of off-screen instances.
public sealed class InstanceTileGrid : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<InstanceTileGrid, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<InstanceTileGrid, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<object?> SelectedItemProperty = AvaloniaProperty.Register<InstanceTileGrid, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    private readonly ItemsRepeater _items;
    private readonly ScrollViewer _scroll;
    private int _columns = 1;
    private INotifyCollectionChanged? _observedSource;
    private readonly HashSet<Control> _realized = new();
    private readonly HashSet<Grid> _realizedRows = new();
    public int ItemCount => ItemsSource?.Cast<object>().Count() ?? 0;
    public int SelectedIndex
    {
        get => ItemsSource?.Cast<object>().ToList().IndexOf(SelectedItem!) ?? -1;
        set { var items = ItemsSource?.Cast<object>().ToArray(); if (items != null && value >= 0 && value < items.Length) SetCurrentValue(SelectedItemProperty, items[value]); }
    }
    public int Columns => _columns;

    public InstanceTileGrid()
    {
        Focusable = true;
        // Virtualize fixed-height rows. UniformGridLayout in Avalonia 11 can
        // mis-estimate distant rows after a width change; StackLayout avoids
        // skipping/repeating tiles when jumping through a large collection.
        _items = new ItemsRepeater { VerticalCacheLength = 0.5, Margin = new Thickness(0, 0, 18, 0),
            Layout = new StackLayout { Orientation = Orientation.Vertical, Spacing = 12 },
            ItemTemplate = new FuncDataTemplate<object[]>((row, _) => BuildRow(row)) };
        _scroll = new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, AllowAutoHide = false };
        Content = _scroll;
        _scroll.SizeChanged += (_, _) => UpdateMetrics();
        _items.ElementPrepared += (_, e) => {
            if (e.Element is Grid row) { _realizedRows.Add(row); foreach (var tile in row.Children) Register(tile); }
            UpdateSelection(); };
        _items.ElementClearing += (_, e) => {
            if (e.Element is Grid row) { _realizedRows.Remove(row); foreach (var tile in row.Children) Unregister(tile); } };
        _items.ElementIndexChanged += (_, _) => UpdateSelection();
        AddHandler(KeyDownEvent, Navigate, RoutingStrategies.Tunnel);
    }

    private void UpdateMetrics()
    {
        double width = _scroll.Bounds.Width - 18;
        if (width <= 0) return;
        int columns = Math.Max(1, (int)Math.Floor((width + 12) / 236));
        if (_columns == columns) return;
        _columns = columns;
        RebuildRows();
    }

    private Grid BuildRow(object[]? items)
    {
        var row = new Grid { Height = 156, ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Range(0, Columns * 2 - 1).Select(i => i % 2 == 0 ? "*" : "12"))) };
        void Populate(object[]? source)
        {
            foreach (var old in row.Children) Unregister(old);
            row.Children.Clear();
            row.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Range(0, Columns * 2 - 1).Select(i => i % 2 == 0 ? "*" : "12")));
            if (source == null) return;
            for (int i = 0; i < source.Length; i++)
            {
                var tile = ItemTemplate?.Build(source[i]);
                if (tile == null) continue;
                tile.DataContext = source[i]; Grid.SetColumn(tile, i * 2); row.Children.Add(tile);
                if (_realizedRows.Contains(row)) Register(tile);
            }
            UpdateSelection();
        }
        Populate(items);
        row.DataContextChanged += (_, _) => Populate(row.DataContext as object[]);
        return row;
    }
    private void Register(Control tile)
    { if (_realized.Add(tile)) tile.AddHandler(PointerPressedEvent, SelectTile, RoutingStrategies.Tunnel); }
    private void Unregister(Control tile)
    { _realized.Remove(tile); tile.RemoveHandler(PointerPressedEvent, SelectTile); tile.Classes.Remove("selected"); }
    private void RebuildRows()
    {
        _items.ItemsSource = ItemsSource?.Cast<object>().Chunk(Columns).ToArray();
    }
    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    { RebuildRows(); _scroll.Offset = default; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_items == null) return;
        if (change.Property == ItemsSourceProperty)
        {
            if (_observedSource != null) _observedSource.CollectionChanged -= SourceChanged;
            _observedSource = ItemsSource as INotifyCollectionChanged;
            if (_observedSource != null) _observedSource.CollectionChanged += SourceChanged;
            RebuildRows();
        }
        if (change.Property == ItemTemplateProperty) RebuildRows();
        if (change.Property == SelectedItemProperty) UpdateSelection();
    }
    private void UpdateSelection()
    {
        foreach (var tile in _realized) tile.Classes.Set("selected", SelectedItem != null && ReferenceEquals(tile.DataContext, SelectedItem));
    }
    private void SelectTile(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control tile && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
        { SetCurrentValue(SelectedItemProperty, tile.DataContext); Focus(NavigationMethod.Pointer); }
    }
    private void Navigate(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || ItemCount == 0) return;
        int rows = Math.Max(1, (int)(_scroll.Viewport.Height / 168));
        int target = e.Key switch {
            Key.Left => SelectedIndex - 1, Key.Right => SelectedIndex + 1,
            Key.Up => SelectedIndex - Columns, Key.Down => SelectedIndex + Columns,
            Key.Home => 0, Key.End => ItemCount - 1,
            Key.PageUp => SelectedIndex - Columns * rows, Key.PageDown => SelectedIndex + Columns * rows,
            _ => -9999 };
        if (target == -9999) return;
        SelectedIndex = Math.Clamp(target, 0, ItemCount - 1);
        ScrollIntoView(SelectedIndex); e.Handled = true;
    }
    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= ItemCount) return;
        if (index == 0) { _scroll.Offset = default; return; }
        if (index == ItemCount - 1) { _scroll.Offset = new Vector(0, _scroll.Extent.Height); return; }
        // Jump by the known uniform row geometry. Forcing an off-screen
        // element into realization first can leave an incorrect layout anchor
        // in Avalonia 11 and make subsequent scrolling jump back to that item.
        double top = index / Columns * 168;
        double bottom = top + 156;
        double offset = _scroll.Offset.Y;
        if (top < offset) offset = top;
        else if (bottom > offset + _scroll.Viewport.Height) offset = bottom - _scroll.Viewport.Height;
        _scroll.Offset = new Vector(0, Math.Max(0, offset));
    }
}
