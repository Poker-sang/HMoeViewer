using System;
using System.Collections.Generic;
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
    private readonly List<SelectionChange> _pendingHistory = [];
    private Task<bool>? _historyTask;
    private bool _isUndoing;
    private bool _isApplyingState;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsRecordingHistory { get; set; }

    private bool CanUndo() => _undoChanges.Count > 0
        && _allItems.Any(item => item.Post.Id == _undoChanges[^1].PostId && item.State == _undoChanges[^1].After
            && (_undoChanges[^1].AfterReviewStage is not { } stage || item.ReviewStage == stage));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!CanUndo())
            return;
        var change = _undoChanges[^1];
        var item = _allItems.First(item => item.Post.Id == change.PostId);
        _undoChanges.RemoveAt(_undoChanges.Count - 1);
        // Undo is itself an auditable transition, but must not become the next undo target.
        _pendingHistory.Add(new SelectionChange(Guid.NewGuid(), change.PostId, change.After, change.Before,
            DateTimeOffset.UtcNow, change.OperationId, change.AfterReviewStage, change.BeforeReviewStage));
        _isUndoing = true;
        try
        {
            _isApplyingState = true;
            try
            {
                item.State = change.Before;
                if (change.BeforeReviewStage is { } stage)
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
            _isUndoing = false;
        }
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void RestoreUndoHistory(IReadOnlyList<SelectionChange> changes)
    {
        _undoChanges.Clear();
        foreach (var change in changes)
        {
            if (change.RevertsOperationId is { } reverted)
                _ = _undoChanges.RemoveAll(entry => entry.OperationId == reverted);
            else
                _undoChanges.Add(change);
        }
        // A crawler refresh or an unsaved previous session must not undo unrelated states.
        while (_undoChanges.Count > 0 && !CanUndo())
            _undoChanges.RemoveAt(_undoChanges.Count - 1);
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
