using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using HMoeData.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HMoeData.Persistence;

public static class HMoeDbStore
{
    private static readonly ValueConverter<Uri, string> _UriConverter = new(
        uri => uri.ToString(),
        value => new Uri(value, UriKind.RelativeOrAbsolute));

    public sealed record SavePostsOptions(DateTimeOffset WriteTime, bool ContinueLatestBatch);

    public sealed class PostLookup : IDisposable
    {
        private readonly PostDbContext? _context;

        internal PostLookup(string databasePath)
        {
            if (!File.Exists(databasePath))
                return;

            _context = CreatePostDbContext(databasePath);
            _ = _context.Database.EnsureCreated();
        }

        public int Count => _context?.Posts.Count() ?? 0;

        public bool Exists(int postId) => _context?.Posts.Any(post => post.Id == postId) ?? false;

        public void Dispose() => _context?.Dispose();
    }

    public static PostLookup OpenPostLookup(string databasePath) => new(databasePath);

    public static IEnumerable<Post> LoadAllPosts(string databasePath)
    {
        if (!File.Exists(databasePath))
            yield break;

        using var context = CreatePostDbContext(databasePath);
        _ = context.Database.EnsureCreated();

        foreach (var post in CreateLoadPostsQuery(context).OrderByDescending(post => post.DateUnixTimeSeconds))
            yield return post;
    }

    public static IEnumerable<Post> LoadNewPosts(string databasePath)
    {
        if (!File.Exists(databasePath))
            yield break;

        using var context = CreatePostDbContext(databasePath);
        _ = context.Database.EnsureCreated();

        var latestWriteTime = context.Posts.Max(post => (long?)post.WriteTimeUnixTimeSeconds);
        if (latestWriteTime is null)
            yield break;

        foreach (var post in CreateLoadPostsQuery(context)
                     .Where(post => post.WriteTimeUnixTimeSeconds == latestWriteTime.Value)
                     .OrderByDescending(post => post.DateUnixTimeSeconds))
            yield return post;
    }

    private static IQueryable<Post> CreateLoadPostsQuery(PostDbContext context)
    {
        return context.Posts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(post => post.DbAuthor)
                .ThenInclude(author => author!.DbRole)
            .Include(post => post.DbAuthor)
                .ThenInclude(author => author!.DbMedals)
            .Include(post => post.DbThumbnail)
            .Include(post => post.DbTags)
            .Include(post => post.DbCats);
    }

    public static async Task SaveSelectionsAsync(
        string databasePath,
        IEnumerable<(int Id, PostSelectionState State, DateTimeOffset WriteTime)> selections,
        CancellationToken token = default)
    {
        var options = new DbContextOptionsBuilder<PostDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString())
            .Options;
        await using var context = new PostDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        foreach (var selection in selections)
        {
            // Viewer edits must never change crawler timestamps or create a new batch.
            var updated = await context.Posts
                .Where(post => post.Id == selection.Id && post.WriteTime == selection.WriteTime)
                .ExecuteUpdateAsync(setters => setters.SetProperty(post => post.IsSelected, selection.State), token)
                .ConfigureAwait(false);
            if (updated is not 1)
                throw new InvalidOperationException($"条目 {selection.Id} 的写入批次已变化或不存在，未保存选择状态。");
        }

        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    public static void SavePosts(string databasePath, IEnumerable<Post> posts, SavePostsOptions options)
    {
        EnsureDirectory(databasePath);

        using var context = CreatePostDbContext(databasePath);
        _ = context.Database.EnsureCreated();

        var postBatch = posts
            .DistinctBy(post => post.Id)
            .ToArray();
        if (postBatch.Length is 0)
            return;

        if (options.ContinueLatestBatch)
            RefreshLatestBatchWriteTime(context, options.WriteTime);

        var postIds = DistinctKeys(postBatch.Select(post => post.Id));
        var authorIds = DistinctKeys(postBatch.Select(post => post.Author.Id));
        var roleNames = DistinctKeys(postBatch.Select(post => post.Author.Role.Name), StringComparer.Ordinal);
        var tagIds = DistinctKeys(postBatch.SelectMany(post => post.Tags).Select(tag => tag.Id));
        var categoryIds = DistinctKeys(postBatch.SelectMany(post => post.Cats).Select(category => category.Id));
        var medalIds = DistinctKeys(postBatch.SelectMany(post => post.Author.Medals).Select(medal => medal.Id), StringComparer.Ordinal);

        var roles = LoadDictionaryByKeys(context.Roles, roleNames, role => role.Name, StringComparer.Ordinal);
        var medals = LoadDictionaryByKeys(context.Medals, medalIds, medal => medal.Id, StringComparer.Ordinal);
        var authors = LoadDictionaryByKeys(context.Authors.Include(author => author.DbMedals), authorIds, author => author.Id);
        var thumbnails = LoadDictionaryByKeys(context.Thumbnails, postIds, thumbnail => thumbnail.DbPostId);
        var tags = LoadDictionaryByKeys(context.Tags, tagIds, tag => tag.Id);
        var categories = LoadDictionaryByKeys(context.Categories, categoryIds, category => category.Id);
        var storedPosts = LoadDictionaryByKeys(
            context.Posts
                .Include(post => post.DbTags)
                .Include(post => post.DbCats),
            postIds,
            post => post.Id);

        foreach (var post in postBatch)
            UpsertPost(context, post, options.WriteTime, roles, medals, authors, thumbnails, tags, categories, storedPosts);

        _ = context.SaveChanges();
    }

    private static void RefreshLatestBatchWriteTime(PostDbContext context, DateTimeOffset writeTime)
    {
        var latestWriteTime = context.Posts.Max(post => (long?)post.WriteTimeUnixTimeSeconds);
        if (latestWriteTime is null)
            return;

        var writeTimeUnixTimeSeconds = writeTime.ToUnixTimeSeconds();
        _ = context.Posts
            .Where(post => post.WriteTimeUnixTimeSeconds == latestWriteTime.Value)
            .ExecuteUpdate(setters => setters
                .SetProperty(post => post.WriteTime, writeTime)
                .SetProperty(post => post.WriteTimeUnixTimeSeconds, writeTimeUnixTimeSeconds));
    }

    private static void UpsertPost(
        PostDbContext context,
        Post source,
        DateTimeOffset writeTime,
        Dictionary<string, Role> roles,
        Dictionary<string, Medal> medals,
        Dictionary<int, Author> authors,
        Dictionary<int, Thumbnail> thumbnails,
        Dictionary<int, Tag> tags,
        Dictionary<int, Category> categories,
        Dictionary<int, Post> storedPosts)
    {
        var role = UpsertRole(context, source.Author.Role, roles);
        var author = UpsertAuthor(context, source.Author, role, medals, authors);
        var thumbnail = UpsertThumbnail(context, source.Id, source.Thumbnail, thumbnails);
        var resolvedTags = ResolveTags(context, source.Tags, tags);
        var resolvedCategories = ResolveCategories(context, source.Cats, categories);
        PreparePostForStore(source, author, thumbnail, writeTime);
        source.Tags = resolvedTags;
        source.Cats = resolvedCategories;

        var target = GetOrAddTrackedEntity(context, source, source.Id, storedPosts);
        target.DbAuthor = author;
        target.DbAuthorId = author.Id;
        target.DbThumbnail = thumbnail;

        ReplaceCollection(target.DbTags, resolvedTags, tag => tag.Id);
        ReplaceCollection(target.DbCats, resolvedCategories, category => category.Id);
    }

    private static void PreparePostForStore(Post source, Author author, Thumbnail thumbnail, DateTimeOffset writeTime)
    {
        source.DateUnixTimeSeconds = source.Date.ToUnixTimeSeconds();
        source.ModifiedDateUnixTimeSeconds = source.ModifiedDate.ToUnixTimeSeconds();
        ApplyWriteTime(source, writeTime);
        source.DbAuthor = author;
        source.DbAuthorId = author.Id;
        source.DbThumbnail = thumbnail;
    }

    private static void ApplyWriteTime(Post target, DateTimeOffset writeTime)
    {
        target.WriteTime = writeTime;
        target.WriteTimeUnixTimeSeconds = writeTime.ToUnixTimeSeconds();
    }

    private static Role UpsertRole(PostDbContext context, Role source, Dictionary<string, Role> roles)
    {
        return GetOrAddTrackedEntity(context, source, source.Name, roles);
    }

    private static Author UpsertAuthor(
        PostDbContext context,
        Author source,
        Role role,
        Dictionary<string, Medal> medals,
        Dictionary<int, Author> authors)
    {
        PrepareAuthorForStore(source, role);
        var resolvedMedals = ResolveMedals(context, source.Medals, medals);
        source.Medals = resolvedMedals;

        var target = GetOrAddTrackedEntity(context, source, source.Id, authors);
        target.DbRole = role;
        target.DbRoleName = role.Name;

        ReplaceCollection(target.DbMedals, resolvedMedals, medal => medal.Id);
        return target;
    }

    private static void PrepareAuthorForStore(Author source, Role role)
    {
        source.DbRole = role;
        source.DbRoleName = role.Name;
    }

    private static Thumbnail UpsertThumbnail(PostDbContext context, int postId, Thumbnail source, Dictionary<int, Thumbnail> thumbnails)
    {
        source.DbPostId = postId;

        return GetOrAddTrackedEntity(context, source, postId, thumbnails);
    }

    private static Tag UpsertTag(PostDbContext context, Tag source, Dictionary<int, Tag> tags)
    {
        return GetOrAddTrackedEntity(context, source, source.Id, tags);
    }

    private static Category UpsertCategory(PostDbContext context, Category source, Dictionary<int, Category> categories)
    {
        return GetOrAddTrackedEntity(context, source, source.Id, categories);
    }

    private static Medal UpsertMedal(PostDbContext context, Medal source, Dictionary<string, Medal> medals)
    {
        return GetOrAddTrackedEntity(context, source, source.Id, medals);
    }

    private static void CopyScalarValues<TEntity>(DbContext context, TEntity target, TEntity source)
        where TEntity : class
    {
        if (!ReferenceEquals(target, source))
            context.Entry(target).CurrentValues.SetValues(source);
    }

    private static Medal[] ResolveMedals(PostDbContext context, IEnumerable<Medal> medals, Dictionary<string, Medal> storedMedals)
    {
        return [.. medals.Select(medal => UpsertMedal(context, medal, storedMedals))];
    }

    private static Tag[] ResolveTags(PostDbContext context, IEnumerable<Tag> tags, Dictionary<int, Tag> storedTags)
    {
        return [.. tags.Select(tag => UpsertTag(context, tag, storedTags))];
    }

    private static Category[] ResolveCategories(PostDbContext context, IEnumerable<Category> categories, Dictionary<int, Category> storedCategories)
    {
        return [.. categories.Select(category => UpsertCategory(context, category, storedCategories))];
    }

    private static TEntity GetOrAddTrackedEntity<TEntity, TKey>(
        DbContext context,
        TEntity source,
        TKey key,
        Dictionary<TKey, TEntity> trackedEntities)
        where TEntity : class
        where TKey : notnull
    {
        if (!trackedEntities.TryGetValue(key, out var target))
        {
            target = source;
            _ = context.Add(target);
            trackedEntities[key] = target;
        }

        CopyScalarValues(context, target, source);
        return target;
    }

    private static TKey[] DistinctKeys<TKey>(IEnumerable<TKey> keys, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        return [.. keys.Distinct(comparer)];
    }

    private static Dictionary<TKey, TEntity> LoadDictionaryByKeys<TEntity, TKey>(
        IQueryable<TEntity> source,
        TKey[] keys,
        Expression<Func<TEntity, TKey>> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TEntity : class
        where TKey : notnull
    {
        if (keys.Length is 0)
            return comparer is null ? new() : new(comparer);

        return WhereKeyIn(source, keySelector, keys)
            .ToDictionary(keySelector.Compile(), comparer ?? EqualityComparer<TKey>.Default);
    }

    private static IQueryable<TEntity> WhereKeyIn<TEntity, TKey>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, TKey>> keySelector,
        TKey[] keys)
    {
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(TKey)],
            Expression.Constant(keys),
            keySelector.Body);

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(containsCall, keySelector.Parameters));
    }

    private static void ReplaceCollection<TItem, TKey>(ICollection<TItem> collection, IEnumerable<TItem> desiredItems, Func<TItem, TKey> keySelector)
        where TKey : notnull
    {
        var desired = desiredItems
            .GroupBy(keySelector)
            .ToDictionary(group => group.Key, group => group.First());
        var existing = collection
            .GroupBy(keySelector)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var existingItem in existing.Where(pair => !desired.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray())
            _ = collection.Remove(existingItem);

        foreach (var desiredItem in desired.Where(pair => !existing.ContainsKey(pair.Key)).Select(pair => pair.Value))
            collection.Add(desiredItem);
    }

    private static void EnsureDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            _ = Directory.CreateDirectory(directory);
    }

    private static PostDbContext CreatePostDbContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<PostDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())
            .Options;
        return new PostDbContext(options);
    }

    internal sealed class PostDbContext(DbContextOptions<PostDbContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts => Set<Post>();

        public DbSet<Author> Authors => Set<Author>();

        public DbSet<Role> Roles => Set<Role>();

        public DbSet<Thumbnail> Thumbnails => Set<Thumbnail>();

        public DbSet<Tag> Tags => Set<Tag>();

        public DbSet<Category> Categories => Set<Category>();

        public DbSet<Medal> Medals => Set<Medal>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<Post>(entity =>
            {
                _ = entity.Property(post => post.Url).HasConversion(_UriConverter);
                _ = entity.HasOne(post => post.DbAuthor)
                    .WithMany()
                    .HasForeignKey(post => post.DbAuthorId)
                    .OnDelete(DeleteBehavior.Restrict);
                _ = entity.HasOne(post => post.DbThumbnail)
                    .WithOne(thumbnail => thumbnail.DbPost)
                    .HasForeignKey<Thumbnail>(thumbnail => thumbnail.DbPostId)
                    .OnDelete(DeleteBehavior.Cascade);
                _ = entity.HasMany(post => post.DbTags)
                    .WithMany();
                _ = entity.HasMany(post => post.DbCats)
                    .WithMany();
            });

            _ = modelBuilder.Entity<Author>(entity =>
            {
                _ = entity.Property(author => author.Url).HasConversion(_UriConverter);
                _ = entity.Property(author => author.AvatarUrl).HasConversion(_UriConverter);
                _ = entity.HasOne(author => author.DbRole)
                    .WithMany()
                    .HasForeignKey(author => author.DbRoleName)
                    .OnDelete(DeleteBehavior.Restrict);
                _ = entity.HasMany(author => author.DbMedals)
                    .WithMany();
            });

            _ = modelBuilder.Entity<Thumbnail>(entity =>
            {
                _ = entity.Property(thumbnail => thumbnail.Url).HasConversion(_UriConverter);
            });

            _ = modelBuilder.Entity<Tag>(entity =>
            {
                _ = entity.Property(tag => tag.Url).HasConversion(_UriConverter);
            });

            _ = modelBuilder.Entity<Category>(entity =>
            {
                _ = entity.Property(category => category.Url).HasConversion(_UriConverter);
            });

            _ = modelBuilder.Entity<Medal>(entity =>
            {
                _ = entity.Property(medal => medal.ImgUrl).HasConversion(_UriConverter);
            });
        }
    }
}
