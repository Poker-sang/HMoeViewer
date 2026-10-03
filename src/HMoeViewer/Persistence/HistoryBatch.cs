using System;

namespace HMoeViewer.Persistence;

internal sealed class HistoryBatch
{
    public int Id { get; set; }

    public required string SourceDatabasePath { get; set; }

    public DateTimeOffset SourceWriteTime { get; set; }
}
