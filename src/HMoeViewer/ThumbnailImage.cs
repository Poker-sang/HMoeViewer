using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace HMoeViewer;

// Read local covers for realized cards, including the virtual panel's offscreen buffer.
// Keep the current source while detached; there is no separate bitmap cache.
public sealed class ThumbnailImage : Image
{
    public static readonly StyledProperty<string?> FilePathProperty = AvaloniaProperty.Register<ThumbnailImage, string?>(nameof(FilePath));

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public static readonly StyledProperty<int> DecodePixelWidthProperty = AvaloniaProperty.Register<ThumbnailImage, int>(nameof(DecodePixelWidth), 400);

    // Zero preserves the original resolution for tooltip previews.
    public int DecodePixelWidth
    {
        get => GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    private Bitmap? _bitmap;
    private string? _loadedPath;
    private int _loadedDecodeWidth;
    private bool _attached;
    private int _generation;
    private static readonly SemaphoreSlim _DecodeSlots = new(4);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LoadImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _ = Interlocked.Increment(ref _generation);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty || change.Property == DecodePixelWidthProperty)
        {
            // Recycled containers must not show artwork belonging to their previous item.
            ClearImage();
            if (_attached)
                LoadImage();
        }
    }

    private async void LoadImage()
    {
        var path = FilePath;
        var decodeWidth = DecodePixelWidth;
        if (!_attached || string.IsNullOrWhiteSpace(path))
            return;
        if (_bitmap is not null && _loadedPath == path && _loadedDecodeWidth == decodeWidth)
            return;
        var generation = Interlocked.Increment(ref _generation);
        try
        {
            var bitmap = await Task.Run(async () =>
            {
                await _DecodeSlots.WaitAsync();
                try
                {
                    if (generation != Volatile.Read(ref _generation))
                        return null;
                    await using var stream = File.OpenRead(path);
                    return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
                }
                finally
                {
                    _ = _DecodeSlots.Release();
                }
            });
            if (generation != _generation || !_attached)
            {
                bitmap?.Dispose();
                return;
            }

            _bitmap = bitmap;
            _loadedPath = path;
            _loadedDecodeWidth = decodeWidth;
            Source = _bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"封面加载失败：{path} — {ex.Message}");
        }
    }

    private void ClearImage()
    {
        _ = Interlocked.Increment(ref _generation);
        Source = null;
        _bitmap = null;
        _loadedPath = null;
    }
}
