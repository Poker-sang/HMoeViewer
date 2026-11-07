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

    /// <summary>当前批次仍可撤回的操作，最新操作在前；与临时历史恢复及 Ctrl+Z 共用同一撤回栈。</summary>
    public ReadOnlyObservableCollection<UndoHistoryEntry> UndoHistory => field ??= new(_undoHistory);

    private void AddUndoChange(SelectionChange change, string title)
    {
        _undoChanges.Add(change);
        _undoHistory.Insert(0, new(change, title));
    }

    /// <summary>撤回产生的恢复栈，下一条可恢复的操作在前；新操作会清空此栈。</summary>
    public ReadOnlyObservableCollection<UndoHistoryEntry> RedoHistory => field ??= new(_redoHistory);

    private void ClearRedoHistory()
    {
        _redoChanges.Clear();
        _redoHistory.Clear();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => CanReplay(_undoChanges);

    private bool CanRedo() => CanReplay(_redoChanges);

    private bool CanReplay(List<SelectionChange> stack) => stack.Count > 0
        && _allItems.Any(item => item.Post.Id == stack[^1].PostId && item.State == stack[^1].After
            && (stack[^1].AfterReviewStage is not { } stage || item.ReviewStage == stage));

    private static SelectionChange ReverseChange(SelectionChange change) => new(
        Guid.NewGuid(), change.PostId, change.After, change.Before, DateTimeOffset.UtcNow,
        change.OperationId, change.AfterReviewStage, change.BeforeReviewStage);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!CanUndo())
            return;
        var change = ReverseChange(_undoChanges[^1]);
        var item = _allItems.First(item => item.Post.Id == change.PostId);
        _undoChanges.RemoveAt(_undoChanges.Count - 1);
        _undoHistory.RemoveAt(0);
        _redoChanges.Add(change);
        _redoHistory.Insert(0, new(change, item.Title, isRedo: true));
        ApplyHistoryChange(item, change);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!CanRedo())
            return;
        var change = ReverseChange(_redoChanges[^1]);
        var item = _allItems.First(item => item.Post.Id == change.PostId);
        _redoChanges.RemoveAt(_redoChanges.Count - 1);
        _redoHistory.RemoveAt(0);
        AddUndoChange(change, item.Title);
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
        // A crawler refresh or an unsaved previous session must not replay unrelated states.
        while (_undoChanges.Count > 0 && !CanUndo())
            _undoChanges.RemoveAt(_undoChanges.Count - 1);
        while (_redoChanges.Count > 0 && !CanRedo())
            _redoChanges.RemoveAt(_redoChanges.Count - 1);
        var titles = _allItems.ToDictionary(item => item.Post.Id, item => item.Title);
        _undoHistory.Clear();
        foreach (var change in _undoChanges.AsEnumerable().Reverse())
            _undoHistory.Add(new(change, titles.GetValueOrDefault(change.PostId, "作品已不在当前批次")));
        _redoHistory.Clear();
        foreach (var change in _redoChanges.AsEnumerable().Reverse())
            _redoHistory.Add(new(change, titles.GetValueOrDefault(change.PostId, "作品已不在当前批次"), isRedo: true));
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
