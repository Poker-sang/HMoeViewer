using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HMoeData.Models;
using HMoeData.Persistence;
using HMoeViewer.Core;
using SQLite;

namespace HMoeViewer.Persistence;

public sealed class SqliteSelectionHistoryStore : ISelectionHistoryStore
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(HistoryRow))]
    public Task<IReadOnlyList<SelectionChange>> ReadAsync(SelectionBatch batch, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<SelectionChange>>(() =>
        {
            var path = GetDatabasePath(batch);
            if (!File.Exists(path))
                return [];
            using var connection = HMoeDbStore.OpenConnection(path);
            EnsureSchema(connection, token);
            var rows = connection.Table<HistoryRow>().OrderBy(row => row.Sequence).ToList();
            token.ThrowIfCancellationRequested();
            return
            [
                .. rows.Select(row => new SelectionChange(Guid.Parse(row.OperationId), row.PostId,
                    row.BeforeState, row.AfterState, DateTimeOffset.Parse(row.ChangedAt, CultureInfo.InvariantCulture),
                    row.RevertsOperationId is null ? null : Guid.Parse(row.RevertsOperationId),
                    row.BeforeReviewStage, row.AfterReviewStage))
            ];
        }, token);

    public Task AppendAsync(SelectionBatch batch, IReadOnlyList<SelectionChange> changes, CancellationToken token = default) =>
        Task.Run(() =>
        {
            if (changes.Count is 0)
                return;
            var path = GetDatabasePath(batch);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var connection = HMoeDbStore.OpenConnection(path);
            EnsureSchema(connection, token);
            connection.RunInTransaction(() =>
            {
                if (connection.Find<BatchRow>(1) is null)
                    _ = connection.Insert(new BatchRow
                    {
                        Id = 1, SourceDatabasePath = Path.GetFullPath(batch.DatabasePath),
                        SourceWriteTime = batch.WriteTime.ToString("O", CultureInfo.InvariantCulture)
                    }, typeof(BatchRow));
                var sequence = connection.Table<HistoryRow>().OrderByDescending(row => row.Sequence).FirstOrDefault()?.Sequence ?? 0;
                foreach (var change in changes)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Enum.IsDefined(change.Before) || !Enum.IsDefined(change.After))
                        throw new ArgumentOutOfRangeException(nameof(changes), "操作历史只能包含四种有效的选择状态。");
                    const TemporaryReviewStage validFlags = TemporaryReviewStage.BothAccepted | TemporaryReviewStage.Viewed | TemporaryReviewStage.Downloaded;
                    if ((change.BeforeReviewStage is { } before && (before & ~validFlags) is not TemporaryReviewStage.None)
                        || (change.AfterReviewStage is { } after && (after & ~validFlags) is not TemporaryReviewStage.None))
                        throw new ArgumentOutOfRangeException(nameof(changes), "临时接受阶段无效。");
                    var operation = change.OperationId.ToString("D");
                    if (connection.Table<HistoryRow>().Where(row => row.OperationId == operation).FirstOrDefault() is not null)
                        continue;
                    _ = connection.Insert(new HistoryRow
                    {
                        Sequence = ++sequence, OperationId = operation, PostId = change.PostId,
                        BeforeState = change.Before, AfterState = change.After,
                        ChangedAt = change.ChangedAt.ToString("O", CultureInfo.InvariantCulture),
                        RevertsOperationId = change.RevertsOperationId?.ToString("D"),
                        BeforeReviewStage = change.BeforeReviewStage, AfterReviewStage = change.AfterReviewStage
                    }, typeof(HistoryRow));
                }
                token.ThrowIfCancellationRequested();
            });
        }, token);

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(HistoryRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BatchRow))]
    private static void EnsureSchema(SQLiteConnection connection, CancellationToken token)
    {
        // sqlite-net adds the nullable stage columns to old history tables without rebuilding them.
        connection.RunInTransaction(() =>
        {
            _ = connection.CreateTable<BatchRow>();
            _ = connection.CreateTable<HistoryRow>();
            if (connection.ExecuteScalar<long>("PRAGMA user_version") < 1)
            {
                foreach (var row in connection.Table<HistoryRow>())
                {
                    token.ThrowIfCancellationRequested();
                    var changed = false;
                    // The old sequential download stage implied both acceptances.
                    if (row.BeforeReviewStage is (TemporaryReviewStage) 2)
                    {
                        row.BeforeReviewStage = TemporaryReviewStage.BothAccepted;
                        changed = true;
                    }
                    if (row.AfterReviewStage is (TemporaryReviewStage) 2)
                    {
                        row.AfterReviewStage = TemporaryReviewStage.BothAccepted;
                        changed = true;
                    }
                    if (changed)
                        _ = connection.Update(row, typeof(HistoryRow));
                }
                _ = connection.Execute("PRAGMA user_version = 1");
            }
            token.ThrowIfCancellationRequested();
        });
    }

    public static string GetDatabasePath(SelectionBatch batch)
    {
        var source = Path.GetFullPath(batch.DatabasePath);
        if (OperatingSystem.IsWindows())
            source = source.ToUpperInvariant();
        var databaseKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return Path.Combine(Path.GetTempPath(), "HMoeViewer", "history", databaseKey,
            batch.WriteTime.UtcTicks.ToString(CultureInfo.InvariantCulture) + ".db");
    }

    [Table("Batch")]
    private sealed class BatchRow
    {
        [PrimaryKey]
        public int Id { get; set; }

        public string SourceDatabasePath { get; set; } = "";

        public string SourceWriteTime { get; set; } = "";
    }

    [Table("SelectionChanges")]
    private sealed class HistoryRow
    {
        [PrimaryKey]
        public long Sequence { get; set; }

        [Indexed("IX_SelectionChanges_OperationId", 0, Unique = true)]
        public string OperationId { get; set; } = "";

        public int PostId { get; set; }

        public PostSelectionState BeforeState { get; set; }

        public PostSelectionState AfterState { get; set; }

        public string ChangedAt { get; set; } = "";

        public string? RevertsOperationId { get; set; }

        public TemporaryReviewStage? BeforeReviewStage { get; set; }

        public TemporaryReviewStage? AfterReviewStage { get; set; }
    }
}
