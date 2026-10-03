using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMoeData.Models;

namespace HMoeViewer.Core;

public partial class ArticleReviewViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _owner;
    private readonly SelectionBatch _batch;
    private readonly IArticleReviewService _service;
    private readonly IReadOnlyList<PostItemViewModel> _items;
    private readonly CancellationTokenSource _closed = new();
    private int _index;

    public ArticleReviewViewModel(MainViewModel owner, PostItemViewModel first)
    {
        _owner = owner;
        IsDetailReview = owner.IsDetail;
        _batch = owner.CurrentBatch ?? throw new InvalidOperationException("尚未加载文章批次。");
        _service = owner.Articles ?? throw new InvalidOperationException("正文服务未配置。");
        var pending = owner.DisplayedItems.Where(i => i.IsPendingDetailReview).ToList();
        var start = pending.IndexOf(first);
        // Opening from another page must show the clicked article, even outside the pending detail queue.
        _items = start < 0 ? [first, .. pending] : [.. pending.Skip(start), .. pending.Take(start)];
    }

    /// <summary>Only windows opened from detail filtering offer acceptance and rejection.</summary>
    public bool IsDetailReview { get; }

    [ObservableProperty]
    public partial string Title { get; set; } = "详情筛选";

    [ObservableProperty]
    public partial string Html { get; set; } = "";

    [ObservableProperty]
    public partial CachedArticle? Article { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingImages { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AcceptCommand)), NotifyCanExecuteChangedFor(nameof(RejectCommand)), NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial bool IsLoaded { get; set; }

    public PostItemViewModel? Current => _index < _items.Count ? _items[_index] : null;

    internal Task<bool> MarkViewedAsync(PostItemViewModel item) => _owner.MarkViewedAsync(item);

    private bool CanLoad() => !IsBusy && Current is not null;

    private bool CanReview() => IsDetailReview && CanLoad();

    private bool CanAccept() => CanReview() && IsLoaded;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        if (!CanLoad())
            return;
        IsBusy = true;
        IsLoaded = false;
        Html = "";
        Article = null;
        IsLoadingImages = false;
        var item = Current!;
        Title = $"{_index + 1}/{_items.Count} · {item.Title}";
        Status = "正在加载正文…";
        try
        {
            var article = await _service.GetArticleAsync(_batch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri), _closed.Token);
            Article = article;
            item.HasCachedBody = true;
            Html = article.Html;
            IsLoaded = true;
            _ = await _owner.MarkViewedAsync(item);
            IsLoadingImages = article.ImageLoads.Values.Any(task => !task.IsCompleted);
            Status = IsLoadingImages ? "正文已显示，图片正在逐张加载…" : "正文已加载。";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Status = $"加载失败，可重试：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        if (Article is { } loaded)
            _ = ObserveImagesAsync(loaded);
    }

    private async Task ObserveImagesAsync(CachedArticle article)
    {
        var errors = await article.ImagesReady;
        if (_closed.IsCancellationRequested || !ReferenceEquals(Article, article))
            return;
        IsLoadingImages = false;
        if (!IsBusy)
        {
            var failures = errors.Count(error => error is not null);
            Status = failures > 0 ? $"正文已显示，{failures} 张图片加载失败，可点击重试加载。" : "正文和图片已加载。";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAccept))]
    public async Task AcceptAsync()
    {
        if (!CanAccept())
            return;
        IsBusy = true;
        var accepted = false;
        var item = Current!;
        Status = "正在扫描正文并本地识别二维码；正文无下载链接时继续获取下载页…";
        try
        {
            var result = item.Acceptance ?? await _service.AcceptAsync(_batch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri), _closed.Token);
            _owner.CompleteAcceptance(item, result);
            accepted = await _owner.MarkDetailAcceptedAsync(item);
            if (!accepted)
                Status = _owner.Status;
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Status = $"收集未完成，仍停留在本篇，可再次接受以重试：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        if (accepted)
            await NextAsync();
    }

    [RelayCommand(CanExecute = nameof(CanReview))]
    public async Task RejectAsync()
    {
        if (!CanReview())
            return;
        Current!.State = PostSelectionState.Deselected;
        await NextAsync();
    }

    private async Task NextAsync()
    {
        do
            ++_index;
        while (Current is { IsPendingDetailReview: false });
        IsLoaded = false;
        Html = "";
        Article = null;
        IsLoadingImages = false;
        AcceptCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
        if (Current is not null && !_closed.IsCancellationRequested)
            await LoadAsync();
        else
        {
            Title = "详情筛选完成";
            Status = "当前队列已处理完毕，可以关闭此窗口。";
        }
    }

    public void Dispose()
    {
        _closed.Cancel();
        GC.SuppressFinalize(this);
    }
}
