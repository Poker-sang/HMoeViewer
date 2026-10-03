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
            {
                if (item!.Acceptance is null)
                {
                    if (Articles is null || CurrentBatch is not { } batch)
                        throw new InvalidOperationException("正文服务尚未就绪。");
                    var result = await Articles.AcceptAsync(batch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri));
                    CompleteAcceptance(item, result);
                }
                _ = await MarkDetailAcceptedAsync(item);
            }
            else
                _ = await SetReviewStageAsync(item!, TemporaryReviewStage.DownloadAccepted);
        }
        catch (Exception ex)
        {
            Status = $"接受失败：{ex.Message}";
        }
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
