using System;
using HMoeData.Models;

namespace HMoeViewer.Core;

public sealed record SelectionChange(
    Guid OperationId,
    int PostId,
    PostSelectionState Before,
    PostSelectionState After,
    DateTimeOffset ChangedAt,
    Guid? RevertsOperationId = null,
    TemporaryReviewStage? BeforeReviewStage = null,
    TemporaryReviewStage? AfterReviewStage = null);
