using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMoeData.Models;
using HMoeViewer.AdvancedObservableCollection;
using HMoeViewer.Extraction;
using Post = HMoeData.Models.Post;

namespace HMoeViewer.Core;

public partial class MainViewModel(IPostRepository repository, ISelectionHistoryStore history) : ObservableObject
{
    private List<PostItemViewModel> _allItems = [];
    private string? _loadedPath;
    private Task<bool>? _saveTask;
    private long _revision;

    [ObservableProperty]
    public partial string DatabasePath { get; set; } = OperatingSystem.IsWindows()
        ? @"D:\HMoeWebCrawler\current.db"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HMoeWebCrawler", "current.db");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrief), nameof(IsDownload), nameof(IsDetail), nameof(IsReview), nameof(IsReselectMode))]
    [NotifyPropertyChangedFor(nameof(IsCardLayout), nameof(IsDownloadListLayout), nameof(ModeHint))]
    [NotifyCanExecuteChangedFor(nameof(CacheAllArticlesCommand), nameof(FetchAllDownloadsCommand), nameof(SendAllToClientCommand))]
    public partial BrowseMode BrowseMode { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool AutoSave { get; set; } = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool HasChanges { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "请选择爬虫数据库，图片从同目录的 img 文件夹读取。";

    public AdvancedObservableCollection<PostItemViewModel> DisplayedItems { get; } = new();

    public int AllCount => _allItems.Count;

    public bool IsBusy => IsLoading || IsSaving || IsRecordingHistory;

    public int SelectedCount => _allItems.Count(i => i.IsSelected);

    public int MatchCount => DisplayedItems.Count;

    public bool IsBrief => BrowseMode is BrowseMode.Brief;

    public bool IsReview => BrowseMode is BrowseMode.Detail or BrowseMode.Download;

    public bool IsReselectMode => ReselectState is not null;

    private PostSelectionState? ReselectState => BrowseMode switch
    {
        BrowseMode.Unselected => PostSelectionState.Unselected,
        BrowseMode.Deselected => PostSelectionState.Deselected,
        BrowseMode.Deleted => PostSelectionState.Deleted,
        _ => null
    };

    public string ModeHint => BrowseMode switch
    {
        BrowseMode.Brief => "点击复选框选择；双击卡片打开原文。",
        BrowseMode.Detail => "点击卡片查看缓存正文；接受后收集下载链接并查看下一篇。",
        BrowseMode.Selected => "显示全部已选项目，包括已接受的项目；点击卡片打开原文。",
        BrowseMode.Unselected or BrowseMode.Deselected or BrowseMode.Deleted => "点击卡片打开原文；点击选中加入已选列表。",
        _ => "打开或复制下载链接；叉按钮标记下载后不需要的条目。"
    };

    partial void OnBrowseModeChanged(BrowseMode value) => RefreshFilter();

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnAutoSaveChanged(bool value)
    {
        if (value && HasChanges)
            _ = SaveAsync();
    }

    private bool CanLoad() => !IsLoading;

    private bool CanSave() => (HasChanges || _pendingHistory.Count > 0) && _loadedPath is not null;

    private bool CanRemove() => IsReview;

    private bool CanSelectPost() => IsReselectMode;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        try
        {
            var path = Path.GetFullPath(DatabasePath);
            // Keep the current batch interactive while asynchronous I/O is in progress.
            if ((HasChanges || _pendingHistory.Count > 0) && !await SaveAsync())
                return;
            var posts = await repository.LoadLatestAsync(path);
            SelectionBatch? batch = posts.Count is 0 ? null : new(path, posts[0].WriteTime);
            var changes = batch is { } loadedBatch ? await history.ReadAsync(loadedBatch) : [];
            var acceptances = new Dictionary<int, PostResult?>();
            if (Articles is not null && batch is { } articleBatch)
                foreach (var post in posts)
                    acceptances[post.Id] = await Articles.ReadAcceptanceAsync(articleBatch, post.Id);
            // Edits made while loading must be saved before replacing the current batch.
            if ((HasChanges || _pendingHistory.Count > 0) && !await SaveAsync())
                return;
            var sameDatabase = string.Equals(path, _loadedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && CurrentBatch?.WriteTime == batch?.WriteTime;
            var currentSelections = sameDatabase ? _allItems.ToDictionary(i => i.Post.Id, i => i.Post.IsSelected) : null;
            var reviewStages = sameDatabase ? _allItems.ToDictionary(i => i.Post.Id, i => i.ReviewStage) : [];
            if (!sameDatabase)
                foreach (var change in changes)
                    if (change.AfterReviewStage is { } stage)
                        reviewStages[change.PostId] = stage;
            var imageDirectory = Path.Combine(Path.GetDirectoryName(path)!, "img");
            var items = posts.OrderByDescending(p => p.Date).Select(p =>
            {
                p.LocalThumbnailPath = Path.Combine(imageDirectory, p.ThumbnailFileName);
                if (currentSelections?.TryGetValue(p.Id, out var state) is true)
                    p.IsSelected = state;
                return new PostItemViewModel(p)
                {
                    Acceptance = acceptances.GetValueOrDefault(p.Id),
                    ReviewStage = reviewStages.GetValueOrDefault(p.Id),
                    HasCachedBody = Articles is not null && batch is { } bodyBatch && Articles.IsBodyCached(bodyBatch, p.Id)
                };
            }).ToList();
            foreach (var item in _allItems)
                item.PropertyChanged -= OnItemChanged;
            _allItems = items;
            DisplayedItems.Source = [.. items];
            foreach (var item in items)
                item.PropertyChanged += OnItemChanged;
            _loadedPath = path;
            DatabasePath = path;
            CurrentBatch = batch;
            CacheAllArticlesCommand.NotifyCanExecuteChanged();
            FetchAllDownloadsCommand.NotifyCanExecuteChanged();
            SendAllToClientCommand.NotifyCanExecuteChanged();
            if (Articles is not null && batch is { } cacheBatch)
                foreach (var item in items.Where(i => i.IsSelected && !i.IsAccepted))
                    Articles.QueueCache(cacheBatch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri));
            _knownStates.Clear();
            foreach (var item in items)
                _knownStates.Add(item.Post.Id, (item.State, item.ReviewStage));
            if (!sameDatabase)
            {
                RestoreUndoHistory(changes);
                // 已删除表示已进入下载后筛选，优先于详情筛选的取消记录。
                BrowseMode = items.Any(item => item.State is PostSelectionState.Deleted || (item.ReviewStage & TemporaryReviewStage.BothAccepted) is not TemporaryReviewStage.None) ? BrowseMode.Download
                    : items.Any(item => item.State is PostSelectionState.Deselected) ? BrowseMode.Detail
                    : BrowseMode.Brief;
            }
            HasChanges = false;
            UndoCommand.NotifyCanExecuteChanged();
            RefreshFilter();
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(SelectedCount));
            Status = posts.Count is 0 ? "数据库中没有可浏览的最新批次。" : $"已加载最新批次，共 {posts.Count} 条。";
        }
        catch (Exception ex)
        {
            Status = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public Task<bool> SaveAsync()
    {
        if (_saveTask is { IsCompleted: false })
            return _saveTask;
        if (!HasChanges || _loadedPath is null)
            return FlushHistoryAsync();
        return _saveTask = SavePendingAsync();
    }

    private async Task<bool> SavePendingAsync()
    {
        IsSaving = true;
        try
        {
            while (HasChanges)
            {
                var revision = _revision;
                var snapshot = _allItems.Select(i => new PostSelection(i.Post.Id, i.State, i.Post.WriteTime)).ToArray();
                // Persist every transition before updating the crawler's selection column.
                if (!await FlushHistoryAsync())
                    return false;
                await repository.SaveAsync(_loadedPath!, snapshot);
                HasChanges = _revision != revision;
            }

            Status = $"已保存 · {DateTime.Now:HH:mm:ss}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"保存失败，修改仍保留在内存中：{ex.Message}";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingState || e.PropertyName is not (nameof(PostItemViewModel.State) or nameof(PostItemViewModel.ReviewStage))
            || sender is not PostItemViewModel item)
            return;
        await HandleItemChangeAsync(item);
    }

    private async Task HandleItemChangeAsync(PostItemViewModel item, bool resetReviewOnReselect = true)
    {
        var before = _knownStates[item.Post.Id];
        var stateChanged = before.State != item.State;
        if (!stateChanged && before.ReviewStage == item.ReviewStage)
            return;
        // Reselecting starts a fresh review; this reset belongs to the same undo operation.
        if (stateChanged && item.IsSelected && !_isUndoing && resetReviewOnReselect)
        {
            _isApplyingState = true;
            try
            {
                item.ReviewStage &= ~TemporaryReviewStage.BothAccepted;
            }
            finally
            {
                _isApplyingState = false;
            }
        }
        _knownStates[item.Post.Id] = (item.State, item.ReviewStage);
        if (stateChanged && item.IsSelected && Articles is not null && CurrentBatch is { } cacheBatch)
            Articles.QueueCache(cacheBatch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri));
        if (!_isUndoing)
        {
            var change = new SelectionChange(Guid.NewGuid(), item.Post.Id, before.State, item.State, DateTimeOffset.UtcNow,
                BeforeReviewStage: before.ReviewStage, AfterReviewStage: item.ReviewStage);
            _pendingHistory.Add(change);
            _undoChanges.Add(change);
        }
        UndoCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        if (stateChanged)
        {
            ++_revision;
            HasChanges = true;
            OnPropertyChanged(nameof(SelectedCount));
        }
        if (stateChanged && !IsBrief || ((before.ReviewStage ^ item.ReviewStage) & TemporaryReviewStage.BothAccepted) is not TemporaryReviewStage.None)
            RefreshFilter();
        _ = FlushHistoryAsync();
        if (stateChanged && AutoSave)
            _ = await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    public void Remove(PostItemViewModel? item)
    {
        if (item is null || !CanRemove() || (IsDetail ? !item.IsPendingDetailReview : !item.IsPendingDownloadReview))
            return;
        item.State = BrowseMode is BrowseMode.Detail ? PostSelectionState.Deselected : PostSelectionState.Deleted;
        if (MatchCount is 0 && SearchText.Length > 0)
            SearchText = "";
    }

    [RelayCommand(CanExecute = nameof(CanSelectPost))]
    public void SelectPost(PostItemViewModel? item)
    {
        if (!CanSelectPost() || item is null || !_allItems.Contains(item) || item.State != ReselectState)
            return;
        item.IsSelected = true;
    }

    public void RefreshFilter()
    {
        var query = SearchText.Trim();
        using (DisplayedItems.DeferFiltersChange())
        {
            DisplayedItems.Filters.Clear();
            DisplayedItems.Filters.Add(IFilter<PostItemViewModel>.Create(i => (BrowseMode switch
            {
                BrowseMode.Brief => true,
                BrowseMode.Detail => i.IsPendingDetailReview,
                BrowseMode.Download => i.IsPendingDownloadReview,
                BrowseMode.Unselected or BrowseMode.Deselected or BrowseMode.Deleted => i.State == ReselectState,
                _ => i.IsSelected
            }) && Matches(i.Post, query), false));
        }
        OnPropertyChanged(nameof(MatchCount));
        RemoveCommand.NotifyCanExecuteChanged();
        SelectPostCommand.NotifyCanExecuteChanged();
        TemporaryAcceptCommand.NotifyCanExecuteChanged();
    }

    private static bool Matches(Post post, string query) => query.Length is 0
        || post.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (post.DbAuthor?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || post.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
        || post.Excerpt.Contains(query, StringComparison.OrdinalIgnoreCase)
        || post.Cats.Any(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
}
