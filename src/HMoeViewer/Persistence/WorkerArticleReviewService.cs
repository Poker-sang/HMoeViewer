using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HMoeViewer.Core;
using HMoeViewer.Extraction;

namespace HMoeViewer.Persistence;

public sealed class WorkerArticleReviewService(BrowserWorkerClient worker) : IArticleReviewService
{
    public event Action<SelectionBatch, int>? BodyCached
    {
        add => worker.BodyCached += value;
        remove => worker.BodyCached -= value;
    }

    public bool IsBodyCached(SelectionBatch batch, int postId) => File.Exists(Path.Combine(ArticleReviewService.GetArticleDirectory(batch, postId), "body.html"));

    public void QueueCache(SelectionBatch batch, Post post) => _ = QueueAsync(batch, post);

    private async Task QueueAsync(SelectionBatch batch, Post post)
    {
        try
        {
            _ = await worker.InvokeAsync(new(Guid.NewGuid().ToString("N"), "queue", batch, post)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A foreground article request reports the failure and can be retried.
        }
    }

    public async Task<PostResult> QueueAcceptanceAsync(SelectionBatch batch, Post post, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return (await worker.InvokeAsync(new(Guid.NewGuid().ToString("N"), "queue-accept", batch, post), token).ConfigureAwait(false)).Result!;
    }

    public async Task WaitForQueuedCacheAsync(CancellationToken token = default) =>
        _ = await worker.InvokeAsync(new(Guid.NewGuid().ToString("N"), "wait"), token).ConfigureAwait(false);

    public Task<CachedArticle> GetArticleAsync(SelectionBatch batch, Post post, CancellationToken token = default) =>
        worker.GetArticleAsync(new(Guid.NewGuid().ToString("N"), "article", batch, post), token);

    public async Task<PostResult> AcceptAsync(SelectionBatch batch, Post post, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return (await worker.InvokeAsync(new(Guid.NewGuid().ToString("N"), "accept", batch, post), token).ConfigureAwait(false)).Result!;
    }

    public async Task<PostResult?> ReadAcceptanceAsync(SelectionBatch batch, int postId, CancellationToken token = default)
    {
        var path = Path.Combine(ArticleReviewService.GetArticleDirectory(batch, postId), "accepted.json");
        if (!File.Exists(path))
            return null;
        var result = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), OutputJsonContext.Default.PostResult);
        if (result is null || result.Id != postId)
            return null;
        LinkCollector.Consolidate(result);
        return result is { Status: "ok", Links.Count: > 0 } ? result : null;
    }
}
