using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using HMoeData.Models;
using SQLite;

namespace HMoeData.Persistence;

public static partial class HMoeDbStore
{
    public static void SavePosts(string databasePath, IEnumerable<Post> posts, SavePostsOptions options)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = OpenConnection(databasePath);
        EnsureSchema(connection);
        var batch = posts.DistinctBy(post => post.Id).ToArray();
        if (batch.Length is 0)
            return;
        connection.RunInTransaction(() =>
        {
            if (options.ContinueLatestBatch && connection.Table<PostRow>().OrderByDescending(row => row.WriteTimeUnixTimeSeconds).FirstOrDefault() is { } latest)
            {
                var seconds = latest.WriteTimeUnixTimeSeconds;
                foreach (var row in connection.Table<PostRow>().Where(row => row.WriteTimeUnixTimeSeconds == seconds))
                {
                    row.WriteTime = TextTime(options.WriteTime);
                    row.WriteTimeUnixTimeSeconds = options.WriteTime.ToUnixTimeSeconds();
                    _ = connection.Update(row, typeof(PostRow));
                }
            }
            foreach (var post in batch)
            {
                var author = post.Author;
                var role = author.Role;
                Upsert(connection, new RoleRow { Name = role.Name, Color = role.Color });
                Upsert(connection, new AuthorRow
                {
                    Id = author.Id, Uid = author.Uid, Name = author.Name, Url = author.Url.ToString(),
                    AvatarUrl = author.AvatarUrl.ToString(), Des = author.Des, CommentsCount = author.CommentsCount,
                    PostsCount = author.PostsCount, Point = author.Point, FollowersCount = author.FollowersCount,
                    FansCount = author.FansCount, DbRoleName = role.Name
                });
                foreach (var medal in author.Medals)
                    Upsert(connection, new MedalRow
                    {
                        Id = medal.Id, ImgUrl = medal.ImgUrl.ToString(), Name = medal.Name, Des = medal.Des, AttrTitle = medal.AttrTitle
                    });
                _ = connection.Table<AuthorMedalRow>().Delete(row => row.AuthorId == author.Id);
                foreach (var medal in author.Medals.DistinctBy(medal => medal.Id))
                    _ = connection.Insert(new AuthorMedalRow { AuthorId = author.Id, DbMedalsId = medal.Id }, typeof(AuthorMedalRow));
                post.DateUnixTimeSeconds = post.Date.ToUnixTimeSeconds();
                post.ModifiedDateUnixTimeSeconds = post.ModifiedDate.ToUnixTimeSeconds();
                post.WriteTime = options.WriteTime;
                post.WriteTimeUnixTimeSeconds = options.WriteTime.ToUnixTimeSeconds();
                post.DbAuthorId = author.Id;
                author.DbRoleName = role.Name;
                Upsert(connection, new PostRow
                {
                    Id = post.Id, Title = post.Title, Url = post.Url.ToString(), Slug = post.Slug, CommentsCount = post.CommentsCount,
                    Type = post.Type, Excerpt = post.Excerpt, Content = post.Content, Views = post.Views,
                    Date = TextTime(post.Date), ModifiedDate = TextTime(post.ModifiedDate), WriteTime = TextTime(post.WriteTime),
                    IsSelected = (int) post.IsSelected, DbAuthorId = post.DbAuthorId, DateUnixTimeSeconds = post.DateUnixTimeSeconds,
                    ModifiedDateUnixTimeSeconds = post.ModifiedDateUnixTimeSeconds, WriteTimeUnixTimeSeconds = post.WriteTimeUnixTimeSeconds
                });
                var thumbnail = post.Thumbnail;
                thumbnail.DbPostId = post.Id;
                Upsert(connection, new ThumbnailRow
                {
                    DbPostId = post.Id, Id = thumbnail.Id, Width = thumbnail.Width, Height = thumbnail.Height,
                    Url = thumbnail.Url.ToString(), Visible = thumbnail.Visible
                });
                foreach (var tag in post.Tags)
                    Upsert(connection, new TagRow
                    {
                        Id = tag.Id, Name = tag.Name, Slug = tag.Slug, Url = tag.Url.ToString(), Description = tag.Description, Count = tag.Count
                    });
                foreach (var category in post.Cats)
                    Upsert(connection, new CategoryRow
                    {
                        Id = category.Id, ParentId = category.ParentId, Name = category.Name, Slug = category.Slug,
                        Uuid = category.Uuid, Url = category.Url.ToString(), Description = category.Description
                    });
                _ = connection.Table<PostTagRow>().Delete(row => row.PostId == post.Id);
                foreach (var tag in post.Tags.DistinctBy(tag => tag.Id))
                    _ = connection.Insert(new PostTagRow { PostId = post.Id, DbTagsId = tag.Id }, typeof(PostTagRow));
                _ = connection.Table<CategoryPostRow>().Delete(row => row.PostId == post.Id);
                foreach (var category in post.Cats.DistinctBy(category => category.Id))
                    _ = connection.Insert(new CategoryPostRow { PostId = post.Id, DbCatsId = category.Id }, typeof(CategoryPostRow));
            }
        });
    }

    private static void Upsert<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(SQLiteConnection connection, T row) where T : class
    {
        // Update/insert instead of replace: replacing a row would cascade-delete its relations.
        if (connection.Update(row, typeof(T)) is 0)
            _ = connection.Insert(row, typeof(T));
    }

    private static void EnsureSchema(SQLiteConnection connection)
    {
        // sqlite-net cannot declare composite primary keys, foreign keys or the original TEXT types.
        // Keep the exact legacy schema for new databases; existing tables are never rebuilt or altered.
        const string schema = """
            CREATE TABLE IF NOT EXISTS Roles (Name TEXT NOT NULL PRIMARY KEY, Color TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Medals (Id TEXT NOT NULL PRIMARY KEY, ImgUrl TEXT NOT NULL, Name TEXT NOT NULL, Des TEXT NOT NULL, AttrTitle TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Tags (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Slug TEXT NOT NULL, Url TEXT NOT NULL, Description TEXT NOT NULL, Count INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS Categories (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, ParentId INTEGER NOT NULL, Name TEXT NOT NULL, Slug TEXT NOT NULL, Uuid TEXT NOT NULL, Url TEXT NOT NULL, Description TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Authors (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Uid TEXT NOT NULL, Name TEXT NOT NULL, Url TEXT NOT NULL,
                AvatarUrl TEXT NOT NULL, Des TEXT NOT NULL, CommentsCount INTEGER NOT NULL, PostsCount INTEGER NOT NULL,
                Point INTEGER NOT NULL, FollowersCount INTEGER NOT NULL, FansCount INTEGER NOT NULL,
                DbRoleName TEXT NOT NULL REFERENCES Roles(Name) ON DELETE RESTRICT);
            CREATE TABLE IF NOT EXISTS AuthorMedal (
                AuthorId INTEGER NOT NULL REFERENCES Authors(Id) ON DELETE CASCADE,
                DbMedalsId TEXT NOT NULL REFERENCES Medals(Id) ON DELETE CASCADE, PRIMARY KEY (AuthorId, DbMedalsId));
            CREATE TABLE IF NOT EXISTS Posts (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, Url TEXT NOT NULL, Slug TEXT NOT NULL,
                CommentsCount INTEGER NOT NULL, Type TEXT NOT NULL, Excerpt TEXT NOT NULL, Content TEXT NOT NULL,
                Date TEXT NOT NULL, ModifiedDate TEXT NOT NULL, Views INTEGER NOT NULL, WriteTime TEXT NOT NULL,
                IsSelected INTEGER NOT NULL, DbAuthorId INTEGER NOT NULL REFERENCES Authors(Id) ON DELETE RESTRICT,
                DateUnixTimeSeconds INTEGER NOT NULL, ModifiedDateUnixTimeSeconds INTEGER NOT NULL, WriteTimeUnixTimeSeconds INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS Thumbnails (
                DbPostId INTEGER NOT NULL PRIMARY KEY REFERENCES Posts(Id) ON DELETE CASCADE,
                Id TEXT NOT NULL, Width TEXT NOT NULL, Height TEXT NOT NULL, Url TEXT NOT NULL, Visible INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS PostTag (
                DbTagsId INTEGER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
                PostId INTEGER NOT NULL REFERENCES Posts(Id) ON DELETE CASCADE, PRIMARY KEY (DbTagsId, PostId));
            CREATE TABLE IF NOT EXISTS CategoryPost (
                DbCatsId INTEGER NOT NULL REFERENCES Categories(Id) ON DELETE CASCADE,
                PostId INTEGER NOT NULL REFERENCES Posts(Id) ON DELETE CASCADE, PRIMARY KEY (DbCatsId, PostId));
            """;
        connection.RunInTransaction(() =>
        {
            foreach (var statement in schema.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _ = connection.Execute(statement);
            _ = connection.CreateIndex("IX_Posts_DateUnixTimeSeconds", "Posts", nameof(PostRow.DateUnixTimeSeconds));
            _ = connection.CreateIndex("IX_Posts_WriteTimeUnixTimeSeconds", "Posts", nameof(PostRow.WriteTimeUnixTimeSeconds));
            _ = connection.CreateIndex("IX_Posts_DbAuthorId", "Posts", nameof(PostRow.DbAuthorId));
            _ = connection.CreateIndex("IX_Authors_DbRoleName", "Authors", nameof(AuthorRow.DbRoleName));
            _ = connection.CreateIndex("IX_AuthorMedal_DbMedalsId", "AuthorMedal", nameof(AuthorMedalRow.DbMedalsId));
            _ = connection.CreateIndex("IX_PostTag_PostId", "PostTag", nameof(PostTagRow.PostId));
            _ = connection.CreateIndex("IX_CategoryPost_PostId", "CategoryPost", nameof(CategoryPostRow.PostId));
        });
    }
}
