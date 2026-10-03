using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HMoeViewer.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HMoeViewer.Persistence;

public sealed class SqliteSelectionHistoryStore : ISelectionHistoryStore
{
    public Task<IReadOnlyList<SelectionChange>> ReadAsync(SelectionBatch batch, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<SelectionChange>>(async () =>
        {
            var path = GetDatabasePath(batch);
            if (!File.Exists(path))
                return [];
            await using var context = new SelectionHistoryDbContext(path, writable: true);
            await EnsureSchemaAsync(context, token).ConfigureAwait(false);
            return await context.SelectionChanges.AsNoTracking().OrderBy(entry => entry.Sequence)
                .Select(entry => new SelectionChange(entry.OperationId, entry.PostId, entry.BeforeState,
                    entry.AfterState, entry.ChangedAt, entry.RevertsOperationId, entry.BeforeReviewStage, entry.AfterReviewStage))
                .ToListAsync(token).ConfigureAwait(false);
        }, token);

    public Task AppendAsync(SelectionBatch batch, IReadOnlyList<SelectionChange> changes, CancellationToken token = default) =>
        Task.Run(async () =>
        {
            if (changes.Count is 0)
                return;
            var path = GetDatabasePath(batch);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var context = new SelectionHistoryDbContext(path, writable: true);
            // Schema creation belongs exclusively to the separate temporary history database.
            await EnsureSchemaAsync(context, token).ConfigureAwait(false);
            await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
            if (!await context.Batch.AnyAsync(token).ConfigureAwait(false))
                _ = context.Batch.Add(new HistoryBatch
                {
                    Id = 1,
                    SourceDatabasePath = Path.GetFullPath(batch.DatabasePath),
                    SourceWriteTime = batch.WriteTime
                });
            var operationIds = changes.Select(change => change.OperationId).ToArray();
            var existing = (await context.SelectionChanges.Where(entry => operationIds.Contains(entry.OperationId))
                .Select(entry => entry.OperationId).ToListAsync(token).ConfigureAwait(false)).ToHashSet();
            var sequence = await context.SelectionChanges.MaxAsync(entry => (long?) entry.Sequence, token)
                .ConfigureAwait(false) ?? 0;
            foreach (var change in changes)
            {
                token.ThrowIfCancellationRequested();
                if (!Enum.IsDefined(change.Before) || !Enum.IsDefined(change.After))
                    throw new ArgumentOutOfRangeException(nameof(changes), "操作历史只能包含四种有效的选择状态。");
                const TemporaryReviewStage validFlags = TemporaryReviewStage.BothAccepted | TemporaryReviewStage.Viewed | TemporaryReviewStage.Downloaded;
                if ((change.BeforeReviewStage is { } before && (before & ~validFlags) is not TemporaryReviewStage.None)
                    || (change.AfterReviewStage is { } after && (after & ~validFlags) is not TemporaryReviewStage.None))
                    throw new ArgumentOutOfRangeException(nameof(changes), "临时接受阶段无效。");
                // Preserve event order and make retries idempotent without raw SQL.
                if (!existing.Add(change.OperationId))
                    continue;
                _ = context.SelectionChanges.Add(new SelectionHistoryEntry
                {
                    Sequence = ++sequence,
                    OperationId = change.OperationId,
                    PostId = change.PostId,
                    BeforeState = change.Before,
                    AfterState = change.After,
                    ChangedAt = change.ChangedAt,
                    RevertsOperationId = change.RevertsOperationId,
                    BeforeReviewStage = change.BeforeReviewStage,
                    AfterReviewStage = change.AfterReviewStage
                });
            }
            _ = await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }, token);

    private static async Task EnsureSchemaAsync(SelectionHistoryDbContext context, CancellationToken token)
    {
        _ = await context.Database.EnsureCreatedAsync(token).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
        var connection = (SqliteConnection) context.Database.GetDbConnection();
        await using var transaction = connection.BeginTransaction(deferred: false);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA table_info('SelectionChanges')";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                _ = columns.Add(reader.GetString(1));
        }
        // Upgrade only the temporary history database; older selection events retain null stages.
        foreach (var column in new[] { nameof(SelectionHistoryEntry.BeforeReviewStage), nameof(SelectionHistoryEntry.AfterReviewStage) })
        {
            if (columns.Contains(column))
                continue;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE SelectionChanges ADD COLUMN {column} INTEGER NULL";
            _ = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (version < 1)
            {
                // The old sequential download stage implied both acceptances; retain that meaning.
                command.CommandText = """
                    UPDATE SelectionChanges SET BeforeReviewStage = 3 WHERE BeforeReviewStage = 2;
                    UPDATE SelectionChanges SET AfterReviewStage = 3 WHERE AfterReviewStage = 2;
                    PRAGMA user_version = 1;
                    """;
                _ = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
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
}
