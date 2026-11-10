using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMoeData.Models;

namespace HMoeViewer.Core;

public partial class MainViewModel
{
    private readonly Dictionary<int, (PostSelectionState State, TemporaryReviewStage ReviewStage)> _knownStates = [];
    private readonly List<SelectionChange> _undoChanges = [];
    private readonly List<SelectionChange> _redoChanges = [];
    private readonly ObservableCollection<UndoHistoryEntry> _redoHistory = [];
    private readonly List<SelectionChange> _pendingHistory = [];
    private readonly ObservableCollection<UndoHistoryEntry> _undoHistory = [];
    private Task<bool>? _historyTask;
    private bool _isReplayingHistory;
    private bool _isApplyingState;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsRecordingHistory { get; set; }

    /// <summary>当前批次的撤回记录，最新操作在前；任意一条均可单独撤回，但执行前必须匹配记录的操作后状态。</summary>
    public ReadOnlyObservableCollection<UndoHistoryEntry> UndoHistory => field ??= new(_undoHistory);

    private void AddUndoChange(SelectionChange change, string title)
    {
        _undoChanges.Add(change);
        _undoHistory.Insert(0, CreateHistoryEntry(change, title));
    }

    /// <summary>撤回产生的恢复栈；任意一条均可在状态匹配时恢复，新操作会清空此栈。</summary>
    public ReadOnlyObservableCollection<UndoHistoryEntry> RedoHistory => field ??= new(_redoHistory);

    private void ClearRedoHistory()
    {
        _redoChanges.Clear();
        _redoHistory.Clear();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => _undoChanges.Count > 0;

    private bool CanRedo() => _redoChanges.Count > 0;

    private UndoHistoryEntry CreateHistoryEntry(SelectionChange change, string title, bool isRedo = false) =>
        new(change, title, () => ReplayHistoryChange(change, isRedo), isRedo);

    private static SelectionChange ReverseChange(SelectionChange change) => new(
        Guid.NewGuid(), change.PostId, change.After, change.Before, DateTimeOffset.UtcNow,
        change.OperationId, change.AfterReviewStage, change.BeforeReviewStage);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (CanUndo())
            ReplayHistoryChange(_undoChanges[^1], isRedo: false);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (CanRedo())
            ReplayHistoryChange(_redoChanges[^1], isRedo: true);
    }

    private void ReplayHistoryChange(SelectionChange original, bool isRedo)
    {
        var stack = isRedo ? _redoChanges : _undoChanges;
        var entries = isRedo ? _redoHistory : _undoHistory;
        var index = stack.IndexOf(original);
        var action = isRedo ? "恢复" : "撤回";
        if (index < 0)
        {
            Status = $"无法{action}：该操作已不在当前记录中。";
            return;
        }
        var item = _allItems.FirstOrDefault(item => item.Post.Id == original.PostId);
        if (item is null)
        {
            Status = $"无法{action} #{original.PostId}：作品已不在当前批次中。";
            return;
        }
        // Validate the entire recorded state, including temporary flags, immediately before replay.
        // A rejected operation must leave the item, both stacks and persisted history untouched.
        if (item.State != original.After || original.AfterReviewStage is { } stage && item.ReviewStage != stage)
        {
            Status = $"无法{action} #{original.PostId}：当前选择状态或临时标记与记录不一致，请先处理相关的后续操作。";
            return;
        }
        var change = ReverseChange(original);
        entries.RemoveAt(stack.Count - 1 - index);
        stack.RemoveAt(index);
        if (isRedo)
            AddUndoChange(change, item.Title);
        else
        {
            _redoChanges.Add(change);
            _redoHistory.Insert(0, CreateHistoryEntry(change, item.Title, isRedo: true));
        }
        Status = $"已{action} #{original.PostId} 的此条操作。";
        ApplyHistoryChange(item, change);
    }

    private void ApplyHistoryChange(PostItemViewModel item, SelectionChange change)
    {
        // Both directions are recorded as reversals, so restart can reconstruct both stacks
        // from the existing temporary history schema without touching the permanent database.
        _pendingHistory.Add(change);
        _isReplayingHistory = true;
        try
        {
            _isApplyingState = true;
            try
            {
                item.State = change.After;
                if (change.AfterReviewStage is { } stage)
                    item.ReviewStage = stage;
            }
            finally
            {
                _isApplyingState = false;
            }
            _ = HandleItemChangeAsync(item);
        }
        finally
        {
            _isReplayingHistory = false;
        }
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RestoreUndoHistory(IReadOnlyList<SelectionChange> changes)
    {
        _undoChanges.Clear();
        _redoChanges.Clear();
        foreach (var change in changes)
        {
            if (change.RevertsOperationId is not { } reverted)
            {
                _undoChanges.Add(change);
                _redoChanges.Clear();
            }
            else if (_undoChanges.RemoveAll(entry => entry.OperationId == reverted) > 0)
                _redoChanges.Add(change);
            else if (_redoChanges.RemoveAll(entry => entry.OperationId == reverted) > 0)
                _undoChanges.Add(change);
        }
        // Keep mismatched records visible: another per-item reversal may make them applicable again.
        // Every replay validates current state, including after restarting or refreshing crawler data.
        var titles = _allItems.ToDictionary(item => item.Post.Id, item => item.Title);
        _undoHistory.Clear();
        foreach (var change in _undoChanges.AsEnumerable().Reverse())
            _undoHistory.Add(CreateHistoryEntry(change, titles.GetValueOrDefault(change.PostId, "作品已不在当前批次")));
        _redoHistory.Clear();
        foreach (var change in _redoChanges.AsEnumerable().Reverse())
            _redoHistory.Add(CreateHistoryEntry(change, titles.GetValueOrDefault(change.PostId, "作品已不在当前批次"), isRedo: true));
        RedoCommand.NotifyCanExecuteChanged();
    }

    private Task<bool> FlushHistoryAsync()
    {
        if (_historyTask is { IsCompleted: false })
            return _historyTask;
        if (_pendingHistory.Count is 0)
            return Task.FromResult(true);
        return _historyTask = PersistHistoryAsync();
    }

    private async Task<bool> PersistHistoryAsync()
    {
        IsRecordingHistory = true;
        try
        {
            if (CurrentBatch is not { } batch)
                throw new InvalidOperationException("尚未加载写入批次，不能记录状态修改。");
            while (_pendingHistory.Count > 0)
            {
                var changes = _pendingHistory.ToArray();
                await history.AppendAsync(batch, changes);
                _pendingHistory.RemoveRange(0, changes.Length);
                SaveCommand.NotifyCanExecuteChanged();
            }
            return true;
        }
        catch (Exception ex)
        {
            Status = $"操作历史保存失败，请点击保存重试：{ex.Message}";
            return false;
        }
        finally
        {
            IsRecordingHistory = false;
        }
    }
}
