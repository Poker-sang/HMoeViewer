using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using HMoeViewer.Extraction;

namespace HMoeViewer.Core;

public sealed record CachedArticle(string Html, string Directory)
{
    public IReadOnlyDictionary<string, Task<string?>> ImageLoads { get; init; } = new Dictionary<string, Task<string?>>();

    public Task<string?[]> ImagesReady => Task.WhenAll(ImageLoads.Values);
}

public interface IArticleReviewService
{
    event Action<SelectionBatch, int>? BodyCached;

    bool IsBodyCached(SelectionBatch batch, int postId);

    void QueueCache(SelectionBatch batch, Post post);

    Task WaitForQueuedCacheAsync(CancellationToken token = default);

    Task<CachedArticle> GetArticleAsync(SelectionBatch batch, Post post, CancellationToken token = default);

    Task<PostResult> AcceptAsync(SelectionBatch batch, Post post, CancellationToken token = default);

    Task<PostResult?> ReadAcceptanceAsync(SelectionBatch batch, int postId, CancellationToken token = default);
}
