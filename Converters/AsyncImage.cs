using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using VortexLauncher.Core.Media;

namespace VortexLauncher.Converters;

public sealed class AsyncImage : Image
{
    public static readonly StyledProperty<string> SourcePathProperty = AvaloniaProperty.Register<AsyncImage, string>(nameof(SourcePath), "");
    public static readonly StyledProperty<int> DecodeWidthProperty = AvaloniaProperty.Register<AsyncImage, int>(nameof(DecodeWidth), 544);
    public string SourcePath { get => GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    private CancellationTokenSource? _loading;
    private ArtworkCache.Lease? _lease;
    private bool _attached;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; Reload(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached = false; Clear(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty || change.Property == DecodeWidthProperty) Reload();
    }
    private void Clear()
    {
        _loading?.Cancel(); _loading?.Dispose(); _loading = null;
        Source = null; _lease?.Dispose(); _lease = null;
    }
    private async void Reload()
    {
        Clear();
        if (!_attached || string.IsNullOrWhiteSpace(SourcePath)) return;
        var cancellation = _loading = new CancellationTokenSource();
        try
        {
            var lease = await ArtworkCache.AcquireAsync(SourcePath, DecodeWidth, cancellation.Token);
            if (!ReferenceEquals(_loading, cancellation) || !_attached) { lease.Dispose(); return; }
            _lease = lease; Source = lease.Bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Artwork could not be loaded: " + ex.Message); }
    }
}
