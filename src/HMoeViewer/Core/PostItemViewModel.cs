using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using HMoeData.Models;
using HMoeViewer.Extraction;

namespace HMoeViewer.Core;

public sealed partial class PostItemViewModel(HMoeData.Models.Post post) : ObservableObject
{
    public HMoeData.Models.Post Post { get; } = post;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingDetailReview), nameof(IsPendingDownloadReview), nameof(IsViewed), nameof(IsDownloaded))]
    public partial TemporaryReviewStage ReviewStage { get; set; }

    public bool IsPendingDetailReview => IsSelected
        && (ReviewStage & TemporaryReviewStage.DetailAccepted) is TemporaryReviewStage.None;

    public bool IsPendingDownloadReview => IsSelected
        && (ReviewStage & TemporaryReviewStage.DownloadAccepted) is TemporaryReviewStage.None;

    public bool IsViewed => (ReviewStage & TemporaryReviewStage.Viewed) is not TemporaryReviewStage.None;

    public bool IsDownloaded => (ReviewStage & TemporaryReviewStage.Downloaded) is not TemporaryReviewStage.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccepted), nameof(DownloadLinks), nameof(CacheState), nameof(CacheStatusText))]
    public partial PostResult? Acceptance { get; set; }

    public bool IsAccepted => Acceptance is not null;

    public IReadOnlyList<Link> DownloadLinks => Acceptance?.Links ?? [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CacheState), nameof(CacheStatusText))]
    public partial bool HasCachedBody { get; set; }

    public PostCacheState CacheState => DownloadLinks.Count > 0 ? PostCacheState.Downloads
        : HasCachedBody ? PostCacheState.Body : PostCacheState.None;

    public string CacheStatusText => CacheState switch
    {
        PostCacheState.None => "没有缓存",
        PostCacheState.Body => "正文已缓存",
        PostCacheState.Downloads => "下载链接已缓存",
        _ => throw new ArgumentOutOfRangeException(nameof(CacheState))
    };

    public string Title => Post.Title;

    public string Metadata => $"#{Post.Id} · {Post.DbAuthor?.Name} · {Post.Date:yyyy-MM-dd}";

    public string Tooltip => $"{Metadata}\n{Title}\n{Post.Excerpt}";

    public string ThumbnailPath => Post.LocalThumbnailPath;

    public PostSelectionState State
    {
        get => Post.IsSelected;
        set
        {
            if (value == State)
                return;
            Post.IsSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsPendingDetailReview));
            OnPropertyChanged(nameof(IsPendingDownloadReview));
        }
    }

    public string StateText => State switch
    {
        PostSelectionState.Unselected => "未选",
        PostSelectionState.Selected => "已选",
        PostSelectionState.Deselected => "取消",
        PostSelectionState.Deleted => "已删",
        _ => throw new ArgumentOutOfRangeException(nameof(State), State, "未知的选择状态。")
    };

    public bool IsSelected
    {
        get => State is PostSelectionState.Selected;
        set
        {
            if (value == IsSelected)
                return;
            State = value ? PostSelectionState.Selected : PostSelectionState.Unselected;
        }
    }
}
