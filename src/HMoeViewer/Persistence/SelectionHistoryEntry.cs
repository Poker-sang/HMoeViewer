using System;
using HMoeData.Models;
using HMoeViewer.Core;

namespace HMoeViewer.Persistence;

internal sealed class SelectionHistoryEntry
{
    public long Sequence { get; set; }

    public Guid OperationId { get; set; }

    public int PostId { get; set; }

    public PostSelectionState BeforeState { get; set; }

    public PostSelectionState AfterState { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid? RevertsOperationId { get; set; }

    public TemporaryReviewStage? BeforeReviewStage { get; set; }

    public TemporaryReviewStage? AfterReviewStage { get; set; }
}
