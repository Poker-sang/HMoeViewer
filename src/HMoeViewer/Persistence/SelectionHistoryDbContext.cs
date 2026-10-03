using System;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HMoeViewer.Persistence;

internal sealed class SelectionHistoryDbContext(string databasePath, bool writable = false) : DbContext
{
    private static readonly ValueConverter<DateTimeOffset, string> _TimeConverter = new(
        value => value.ToString("O", CultureInfo.InvariantCulture),
        value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture));

    private static readonly ValueConverter<Guid, string> _OperationConverter = new(
        value => value.ToString("D"),
        value => Guid.Parse(value));

    public DbSet<SelectionHistoryEntry> SelectionChanges => Set<SelectionHistoryEntry>();

    public DbSet<HistoryBatch> Batch => Set<HistoryBatch>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = writable ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<SelectionHistoryEntry>(entity =>
        {
            _ = entity.HasKey(entry => entry.Sequence);
            _ = entity.Property(entry => entry.Sequence).ValueGeneratedNever();
            _ = entity.HasIndex(entry => entry.OperationId).IsUnique();
            _ = entity.Property(entry => entry.OperationId).HasConversion(_OperationConverter);
            _ = entity.Property(entry => entry.RevertsOperationId).HasConversion(_OperationConverter);
            _ = entity.Property(entry => entry.ChangedAt).HasConversion(_TimeConverter);
        });
        _ = modelBuilder.Entity<HistoryBatch>(entity =>
        {
            _ = entity.Property(batch => batch.Id).ValueGeneratedNever();
            _ = entity.Property(batch => batch.SourceWriteTime).HasConversion(_TimeConverter);
        });
    }
}
