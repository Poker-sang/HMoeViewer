using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMoeViewer.Extraction;

namespace HMoeViewer.Core;

public partial class MainViewModel
{
    private readonly SemaphoreSlim _articleBatchGate = new(1);

    public MainViewModel(IPostRepository repository, ISelectionHistoryStore history, IArticleReviewService articles) : this(repository, history)
    {
        Articles = articles;
        articles.BodyCached += (batch, postId) => Dispatcher.UIThread.Post(() =>
        {
            if (CurrentBatch == batch && _allItems.FirstOrDefault(item => item.Post.Id == postId) is { } item)
                item.HasCachedBody = true;
        });
    }

    public bool IsDownload => BrowseMode is BrowseMode.Download;

    public bool IsDetail => BrowseMode is BrowseMode.Detail;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCardLayout), nameof(IsDownloadListLayout))]
    public partial DownloadLayoutMode DownloadLayout { get; set; }

    public bool IsCardLayout => !IsDownload || DownloadLayout is DownloadLayoutMode.Cards;

    public bool IsDownloadListLayout => IsDownload && DownloadLayout is not DownloadLayoutMode.Cards;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CacheAllArticlesCommand)), NotifyCanExecuteChangedFor(nameof(FetchAllDownloadsCommand))]
    public partial bool IsBulkBusy { get; set; }

    private bool CanCacheAllArticles() => !IsBulkBusy && IsDetail && Articles is not null && CurrentBatch is not null;

    private bool CanFetchAllDownloads() => !IsBulkBusy && IsDownload && Articles is not null && CurrentBatch is not null;

    [RelayCommand(CanExecute = nameof(CanCacheAllArticles))]
    public Task CacheAllArticlesAsync() => RunArticleBatchAsync(false);

    [RelayCommand(CanExecute = nameof(CanFetchAllDownloads))]
    public Task FetchAllDownloadsAsync() => RunArticleBatchAsync(true);

    private async Task RunArticleBatchAsync(bool downloads)
    {
        if (IsBulkBusy || Articles is null || CurrentBatch is not { } batch)
            return;
        var items = _allItems.Where(item => downloads ? item.IsPendingDownloadReview : item.IsPendingDetailReview).ToArray();
        var succeeded = 0;
        var failed = 0;
        string? lastError = null;
        IsBulkBusy = true;
        await _articleBatchGate.WaitAsync();
        try
        {
            for (var index = 0; index < items.Length; ++index)
            {
                var item = items[index];
                Status = $"{(downloads ? "获取下载链接" : "缓存正文")} {index + 1}/{items.Length}：{item.Title}";
                try
                {
                    var post = new Post(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri);
                    if (downloads)
                    {
                        var result = await Articles.AcceptAsync(batch, post);
                        if (CurrentBatch == batch && _allItems.Contains(item))
                            CompleteAcceptance(item, result);
                    }
                    else
                    {
                        var article = await Articles.GetArticleAsync(batch, post);
                        item.HasCachedBody = true;
                        var errors = await article.ImagesReady;
                        if (errors.Any(error => error is not null))
                            throw new InvalidOperationException("部分图片加载失败，正文已缓存，可再次点击重试。");
                    }
                    ++succeeded;
                }
                catch (OperationCanceledException)
                {
                    Status = "批量操作已取消。";
                    return;
                }
                catch (Exception ex)
                {
                    ++failed;
                    lastError = $"#{item.Post.Id}：{ex.Message}";
                }
            }
            Status = $"{(downloads ? "获取下载链接" : "缓存正文")}完成：成功 {succeeded}，失败 {failed}。"
                + (lastError is null ? "" : $" 最近错误：{lastError}");
        }
        finally
        {
            IsBulkBusy = false;
            _ = _articleBatchGate.Release();
        }
    }

    public SelectionBatch? CurrentBatch { get; private set; }

    public IArticleReviewService? Articles { get; }

    public void CompleteAcceptance(PostItemViewModel item, PostResult result)
    {
        item.Acceptance = result;
        RefreshFilter();
    }
}
