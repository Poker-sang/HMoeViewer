using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using HMoeData.Models;

namespace HMoeViewer.Core;

/// <summary>撤回或恢复记录的显示项及单条操作命令；执行时校验当前状态，不向永久数据库添加字段。</summary>
public sealed class UndoHistoryEntry(SelectionChange change, string title, Action replay, bool isRedo = false)
{
    public IRelayCommand ReplayCommand { get; } = new RelayCommand(replay);

    public bool IsRedo => isRedo;

    public string ActionLabel => isRedo ? "恢复此条" : "撤回此条";

    public int PostId => change.PostId;

    public string Title { get; } = title;

    public string TimeText => change.ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string Description { get; } = Describe(isRedo ? change with
    {
        Before = change.After,
        After = change.Before,
        BeforeReviewStage = change.AfterReviewStage,
        AfterReviewStage = change.BeforeReviewStage
    } : change);

    private static string Describe(SelectionChange change)
    {
        var changes = new List<string>();
        if (change.Before != change.After)
            changes.Add($"{StateText(change.Before)} → {StateText(change.After)}");
        if (change.BeforeReviewStage is { } before && change.AfterReviewStage is { } after)
        {
            AddFlagChange(TemporaryReviewStage.DetailAccepted, "详情临时接受");
            AddFlagChange(TemporaryReviewStage.DownloadAccepted, "下载临时接受");
            AddFlagChange(TemporaryReviewStage.Viewed, "已查看");
            AddFlagChange(TemporaryReviewStage.Downloaded, "已发送到客户端");

            void AddFlagChange(TemporaryReviewStage flag, string label)
            {
                if (before.Has(flag) != after.Has(flag))
                    changes.Add($"{(after.Has(flag) ? "标记" : "清除")}：{label}");
            }
        }
        return string.Join("；", changes);
    }

    private static string StateText(PostSelectionState state) => state switch
    {
        PostSelectionState.Unselected => "未选择",
        PostSelectionState.Selected => "已选",
        PostSelectionState.Deselected => "取消",
        PostSelectionState.Deleted => "已删",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知的选择状态。")
    };
}
