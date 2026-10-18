using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HMoeData.Models;
using HMoeData.Persistence;
using HMoeViewer.Core;

namespace HMoeViewer.Persistence;

public sealed class SqlitePostRepository : IPostRepository
{
    public Task<IReadOnlyList<Post>> LoadLatestAsync(string databasePath) => Task.Run<IReadOnlyList<Post>>(() =>
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("数据库不存在，请选择 current.db。", databasePath);
        var posts = HMoeDbStore.LoadNewPosts(databasePath).ToList();
        if (posts.Count is 0)
            return [];
        // The indexed seconds column may contain more than one precise batch timestamp.
        var batchTime = posts.Max(post => post.WriteTime);
        return [.. posts.Where(post => post.WriteTime == batchTime)];
    });

    public Task SaveAsync(string databasePath, IReadOnlyList<PostSelection> selections) => selections.Count is 0
        ? Task.CompletedTask
        : HMoeDbStore.SaveSelectionsAsync(databasePath,
            selections.Select(selection => (selection.Id, selection.State, selection.WriteTime)));
}
