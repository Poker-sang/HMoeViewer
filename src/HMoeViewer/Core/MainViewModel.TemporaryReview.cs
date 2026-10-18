using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HMoeData.Models;

namespace HMoeViewer.Core;

public partial class MainViewModel
{
    private bool CanTemporaryAccept(PostItemViewModel? item) => item is not null && _allItems.Contains(item)
        && (IsDetail && item.IsPendingDetailReview || IsDownload && item.IsPendingDownloadReview);

    [RelayCommand(CanExecute = nameof(CanTemporaryAccept))]
    public async Task TemporaryAcceptAsync(PostItemViewModel? item)
    {
        if (!CanTemporaryAccept(item))
            return;
        try
        {
            if (IsDetail)
                _ = await AcceptDetailAsync(item!);
            else
                _ = await SetReviewStageAsync(item!, TemporaryReviewStage.DownloadAccepted);
        }
        catch (Exception ex)
        {
            Status = $"接受失败：{ex.Message}";
        }
    }

    /// <summary>
    /// Records temporary detail acceptance before queuing download extraction.
    /// The shared body queue owns extraction; leaving the article window does not cancel it.
    /// </summary>
    public async Task<bool> AcceptDetailAsync(PostItemViewModel item)
    {
        if (Articles is null || CurrentBatch is not { } batch)
            throw new InvalidOperationException("正文服务尚未就绪。");
        if (!await MarkDetailAcceptedAsync(item))
            return false;
        if (item.Acceptance is null)
        {
            Status = $"#{item.Post.Id} 已接受，下载链接已加入后台队列。";
            QueueDownloadCollection(batch, item);
        }
        return true;
    }

    public Task<bool> MarkDetailAcceptedAsync(PostItemViewModel item) =>
        SetReviewStageAsync(item, TemporaryReviewStage.DetailAccepted, restoreSelection: true);

    public Task<bool> MarkViewedAsync(PostItemViewModel item) => SetReviewStageAsync(item, TemporaryReviewStage.Viewed);

    public Task<bool> MarkDownloadedAsync(PostItemViewModel item) => SetReviewStageAsync(item, TemporaryReviewStage.Downloaded);

    private async Task<bool> SetReviewStageAsync(PostItemViewModel item, TemporaryReviewStage stage, bool restoreSelection = false)
    {
        if (!_allItems.Contains(item))
            return false;
        // Each stage is independent; completing one must preserve the other acceptance.
        if ((item.ReviewStage & stage) == stage)
            return await FlushHistoryAsync();
        // Selection restoration and temporary acceptance form one Ctrl+Z operation.
        _isApplyingState = true;
        try
        {
            if (restoreSelection)
                item.State = PostSelectionState.Selected;
            item.ReviewStage |= stage;
        }
        finally
        {
            _isApplyingState = false;
        }
        await HandleItemChangeAsync(item, resetReviewOnReselect: false);
        return await FlushHistoryAsync();
    }
}
