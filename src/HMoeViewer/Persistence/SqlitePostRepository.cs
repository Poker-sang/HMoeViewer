using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HMoeData.Models;
using HMoeData.Persistence;
using HMoeViewer.Core;
using Microsoft.EntityFrameworkCore;

namespace HMoeViewer.Persistence;

public sealed class SqlitePostRepository : IPostRepository
{
    public Task<IReadOnlyList<Post>> LoadLatestAsync(string databasePath) => Task.Run<IReadOnlyList<Post>>(async () =>
        {
            if (!File.Exists(databasePath))
                throw new FileNotFoundException("数据库不存在，请选择 current.db。", databasePath);
            await using var context = new ViewerDbContext(databasePath);
            var latestWriteTime = await context.Posts.MaxAsync(post => (long?) post.WriteTimeUnixTimeSeconds).ConfigureAwait(false);
            if (latestWriteTime is null)
                return [];
            var posts = await context.Posts.AsNoTracking().AsSplitQuery()
                .Include(post => post.DbAuthor).ThenInclude(author => author!.DbRole)
                .Include(post => post.DbAuthor).ThenInclude(author => author!.DbMedals)
                .Include(post => post.DbThumbnail)
                .Include(post => post.DbTags)
                .Include(post => post.DbCats)
                .Where(post => post.WriteTimeUnixTimeSeconds == latestWriteTime.Value)
                .OrderByDescending(post => post.DateUnixTimeSeconds)
                .ToListAsync().ConfigureAwait(false);
            if (posts.Count is 0)
                return [];
            // The indexed seconds column can contain more than one precise WriteTime.
            var batchTime = posts.Max(post => post.WriteTime);
            return [.. posts.Where(post => post.WriteTime == batchTime)];
        });

    public Task SaveAsync(string databasePath, IReadOnlyList<PostSelection> selections) => selections.Count is 0
        ? Task.CompletedTask
        : Task.Run(() => HMoeDbStore.SaveSelectionsAsync(databasePath,
            selections.Select(selection => (selection.Id, selection.State, selection.WriteTime))));
}
