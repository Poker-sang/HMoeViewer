using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HMoeData.Models;
using SQLite;

namespace HMoeData.Persistence;

public static partial class HMoeDbStore
{
    public sealed record SavePostsOptions(DateTimeOffset WriteTime, bool ContinueLatestBatch);

    public sealed class PostLookup : IDisposable
    {
        private readonly SQLiteConnection? _connection;

        internal PostLookup(string databasePath)
        {
            if (!File.Exists(databasePath))
                return;
            _connection = OpenConnection(databasePath);
            EnsureSchema(_connection);
        }

        public int Count => _connection?.Table<PostRow>().Count() ?? 0;

        public bool Exists(int postId) => _connection?.Find<PostRow>(postId) is not null;

        public void Dispose() => _connection?.Dispose();
    }

    public static PostLookup OpenPostLookup(string databasePath) => new(databasePath);

    public static IEnumerable<Post> LoadAllPosts(string databasePath) => LoadPosts(databasePath, false);

    public static IEnumerable<Post> LoadNewPosts(string databasePath) => LoadPosts(databasePath, true);

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PostRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AuthorRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ThumbnailRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TagRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CategoryRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MedalRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RoleRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PostTagRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CategoryPostRow))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AuthorMedalRow))]
    private static IReadOnlyList<Post> LoadPosts(string databasePath, bool latestOnly)
    {
        if (!File.Exists(databasePath))
            return [];
        using var connection = OpenConnection(databasePath, readOnly: true);
        IReadOnlyList<Post> posts = [];
        // Read all relation tables from one snapshot while the crawler may be writing.
        connection.RunInTransaction(() => posts = ReadPosts(connection, latestOnly));
        return posts;
    }

    private static IReadOnlyList<Post> ReadPosts(SQLiteConnection connection, bool latestOnly)
    {
        var query = connection.Table<PostRow>();
        if (latestOnly)
        {
            var latest = query.OrderByDescending(row => row.WriteTimeUnixTimeSeconds).FirstOrDefault();
            if (latest is null)
                return [];
            var seconds = latest.WriteTimeUnixTimeSeconds;
            query = query.Where(row => row.WriteTimeUnixTimeSeconds == seconds);
        }
        var rows = query.OrderByDescending(row => row.DateUnixTimeSeconds).ToList();
        if (rows.Count is 0)
            return [];
        var postIds = rows.Select(row => row.Id).ToList();
        var authorIds = rows.Select(row => row.DbAuthorId).Distinct().ToList();
        var roles = connection.Table<RoleRow>().ToDictionary(row => row.Name, row => new Role { Name = row.Name, Color = row.Color });
        var authors = connection.Table<AuthorRow>().Where(row => authorIds.Contains(row.Id)).ToDictionary(row => row.Id, row => new Author
        {
            Id = row.Id, Uid = row.Uid, Name = row.Name, Url = Uri(row.Url), AvatarUrl = Uri(row.AvatarUrl),
            Des = row.Des, CommentsCount = row.CommentsCount, PostsCount = row.PostsCount, Point = row.Point,
            FollowersCount = row.FollowersCount, FansCount = row.FansCount, Medals = [], Role = roles[row.DbRoleName]
        });
        var thumbnails = connection.Table<ThumbnailRow>().Where(row => postIds.Contains(row.DbPostId)).ToDictionary(row => row.DbPostId, row => new Thumbnail
        {
            DbPostId = row.DbPostId, Id = row.Id, Width = row.Width, Height = row.Height, Url = Uri(row.Url), Visible = row.Visible
        });
        var posts = rows.Select(row => new Post
        {
            Id = row.Id, Title = row.Title, Url = Uri(row.Url), Slug = row.Slug, CommentsCount = row.CommentsCount,
            Type = row.Type, Excerpt = row.Excerpt, Content = row.Content, Views = row.Views,
            Date = Time(row.Date), ModifiedDate = Time(row.ModifiedDate), WriteTime = Time(row.WriteTime),
            DateUnixTimeSeconds = row.DateUnixTimeSeconds, ModifiedDateUnixTimeSeconds = row.ModifiedDateUnixTimeSeconds,
            WriteTimeUnixTimeSeconds = row.WriteTimeUnixTimeSeconds, IsSelected = (PostSelectionState) row.IsSelected,
            Author = authors[row.DbAuthorId], Thumbnail = thumbnails[row.Id], Tags = [], Cats = []
        }).ToList();
        var byId = posts.ToDictionary(post => post.Id);
        var tagLinks = connection.Table<PostTagRow>().Where(row => postIds.Contains(row.PostId)).ToList();
        var tagIds = tagLinks.Select(row => row.DbTagsId).Distinct().ToList();
        var tags = connection.Table<TagRow>().Where(row => tagIds.Contains(row.Id)).ToDictionary(row => row.Id, row => new Tag
        {
            Id = row.Id, Name = row.Name, Slug = row.Slug, Url = Uri(row.Url), Description = row.Description, Count = row.Count
        });
        foreach (var link in tagLinks)
            byId[link.PostId].DbTags.Add(tags[link.DbTagsId]);
        var categoryLinks = connection.Table<CategoryPostRow>().Where(row => postIds.Contains(row.PostId)).ToList();
        var categoryIds = categoryLinks.Select(row => row.DbCatsId).Distinct().ToList();
        var categories = connection.Table<CategoryRow>().Where(row => categoryIds.Contains(row.Id)).ToDictionary(row => row.Id, row => new Category
        {
            Id = row.Id, ParentId = row.ParentId, Name = row.Name, Slug = row.Slug,
            Uuid = row.Uuid, Url = Uri(row.Url), Description = row.Description
        });
        foreach (var link in categoryLinks)
            byId[link.PostId].DbCats.Add(categories[link.DbCatsId]);
        var medalLinks = connection.Table<AuthorMedalRow>().Where(row => authorIds.Contains(row.AuthorId)).ToList();
        var medalIds = medalLinks.Select(row => row.DbMedalsId).Distinct().ToList();
        var medals = connection.Table<MedalRow>().Where(row => medalIds.Contains(row.Id)).ToDictionary(row => row.Id, row => new Medal
        {
            Id = row.Id, ImgUrl = Uri(row.ImgUrl), Name = row.Name, Des = row.Des, AttrTitle = row.AttrTitle
        });
        foreach (var link in medalLinks)
            authors[link.AuthorId].DbMedals.Add(medals[link.DbMedalsId]);
        foreach (var post in posts)
            post.Thumbnail.DbPost = post;
        return posts;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PostSelectionRow))]
    public static Task SaveSelectionsAsync(string databasePath,
        IEnumerable<(int Id, PostSelectionState State, DateTimeOffset WriteTime)> selections, CancellationToken token = default) =>
        Task.Run(() =>
        {
            using var connection = OpenConnection(databasePath, readOnly: false, create: false);
            connection.RunInTransaction(() =>
            {
                foreach (var selection in selections)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Enum.IsDefined(selection.State))
                        throw new ArgumentOutOfRangeException(nameof(selections), "选择状态无效。");
                    var row = connection.Find<PostRow>(selection.Id);
                    if (row is null || Time(row.WriteTime) != selection.WriteTime)
                        throw new InvalidOperationException($"条目 {selection.Id} 的写入批次已变化或不存在，未保存选择状态。");
                    if (connection.Update(new PostSelectionRow { Id = selection.Id, IsSelected = (int) selection.State }, typeof(PostSelectionRow)) is not 1)
                        throw new InvalidOperationException($"条目 {selection.Id} 未保存选择状态。");
                }
                token.ThrowIfCancellationRequested();
            });
        }, token);

    public static SQLiteConnection OpenConnection(string databasePath, bool readOnly = false, bool create = true)
    {
        var flags = readOnly ? SQLiteOpenFlags.ReadOnly : SQLiteOpenFlags.ReadWrite;
        if (!readOnly && create)
            flags |= SQLiteOpenFlags.Create;
        var connection = new SQLiteConnection(databasePath, flags | SQLiteOpenFlags.FullMutex);
        try
        {
            connection.BusyTimeout = TimeSpan.FromSeconds(30);
            _ = connection.Execute("PRAGMA foreign_keys = ON");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static Uri Uri(string value) => new(value, UriKind.RelativeOrAbsolute);

    private static DateTimeOffset Time(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string TextTime(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
}
