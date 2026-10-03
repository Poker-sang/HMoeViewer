using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HMoeViewer.Core;

namespace HMoeViewer.Controls;

public sealed class ArticleImage : Border
{
    private readonly string _source;
    private readonly CachedArticle? _article;
    private readonly TextBlock _placeholder = new() { Text = "图片加载中…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _loading;
    private Bitmap? _bitmap;
    private static readonly SemaphoreSlim _DecodeSlots = new(4);

    public ArticleImage(string source, CachedArticle? article)
    {
        _source = source;
        _article = article;
        MaxWidth = 584;
        MaxHeight = 480;
        Width = 584;
        Height = 100;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _placeholder;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _loading = new CancellationTokenSource();
        LoadAsync(_loading.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
        Child = _placeholder;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void LoadAsync(CancellationToken token)
    {
        try
        {
            if (!Uri.TryCreate(_source, UriKind.Absolute, out var uri) || !uri.IsFile)
                throw new InvalidDataException("图片没有本地缓存地址。");
            var path = uri.LocalPath;
            if (_article?.ImageLoads.TryGetValue(path, out var pending) is true
                && await pending.WaitAsync(token) is { } error)
                throw new IOException(error);
            var bitmap = await Task.Run(async () =>
            {
                await _DecodeSlots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, 584);
                }
                finally
                {
                    _ = _DecodeSlots.Release();
                }
            }, token);
            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }
            _bitmap = bitmap;
            var scale = Math.Min(1, Math.Min(MaxWidth / bitmap.Size.Width, MaxHeight / bitmap.Size.Height));
            Width = bitmap.Size.Width * scale;
            Height = bitmap.Size.Height * scale;
            Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _placeholder.Text = "图片加载失败，可点击上方“重试加载”。";
                ToolTip.SetTip(this, ex.Message);
            }
        }
    }
}
