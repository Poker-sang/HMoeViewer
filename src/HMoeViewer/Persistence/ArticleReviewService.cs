using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using HMoeViewer.Core;
using HMoeViewer.Extraction;
using Microsoft.Playwright;
using Extractor = HMoeViewer.Extraction.Extractor;
using OutputJsonContext = HMoeViewer.Extraction.OutputJsonContext;
using Post = HMoeViewer.Extraction.Post;
using QrReader = HMoeViewer.Extraction.QrReader;
using Resource = HMoeViewer.Extraction.Resource;
using ResourceFetcher = HMoeViewer.Extraction.ResourceFetcher;

namespace HMoeViewer.Persistence;

public sealed class ArticleReviewService : IArticleReviewService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>();
    private readonly ConcurrentDictionary<string, byte> _queued = new();
    private readonly Task _worker;
    private int _foreground;
    private readonly ConcurrentDictionary<string, Lazy<CacheOperation>> _operations = new();

    private sealed class CacheOperation
    {
        public TaskCompletionSource<CachedArticle> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion { get; set; } = Task.CompletedTask;
    }

    private CacheOperation StartCache(SelectionBatch batch, Post post)
    {
        var key = GetArticleDirectory(batch, post.Id);
        // Completed attempts can be retried; active prefetch and foreground readers share one job.
        if (_operations.TryGetValue(key, out var previous) && previous.IsValueCreated && previous.Value.Completion.IsCompleted)
            _ = _operations.TryRemove(new KeyValuePair<string, Lazy<CacheOperation>>(key, previous));
        return _operations.GetOrAdd(key, _ => new Lazy<CacheOperation>(() =>
        {
            var operation = new CacheOperation();
            operation.Completion = Task.Run(() => CacheAsync(batch, post, operation.Ready, _shutdown.Token));
            return operation;
        })).Value;
    }

    public ArticleReviewService() => _worker = Task.Run(PrefetchAsync);

    public event Action<SelectionBatch, int>? BodyCached;

    public bool IsBodyCached(SelectionBatch batch, int postId) => File.Exists(Path.Combine(GetArticleDirectory(batch, postId), "body.html"));

    public static string GetArticleDirectory(SelectionBatch batch, int postId) =>
        Path.ChangeExtension(SqliteSelectionHistoryStore.GetDatabasePath(batch), "articles") + Path.DirectorySeparatorChar + postId;

    public void QueueCache(SelectionBatch batch, Post post)
    {
        var key = GetArticleDirectory(batch, post.Id);
        if (!File.Exists(Path.Combine(key, "images.complete")) && _queued.TryAdd(key, 0))
            _ = _queue.Writer.TryWrite(async () =>
            {
                try
                {
                    // Give an opened article priority over the remaining background queue.
                    while (Volatile.Read(ref _foreground) > 0)
                        await Task.Delay(100, _shutdown.Token).ConfigureAwait(false);
                    var operation = StartCache(batch, post);
                    _ = await operation.Ready.Task.ConfigureAwait(false);
                    await operation.Completion.ConfigureAwait(false);
                }
                finally
                {
                    _ = _queued.TryRemove(GetArticleDirectory(batch, post.Id), out _);
                }
            });
    }

    public async Task WaitForQueuedCacheAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A marker waits for all earlier prefetch jobs, including their images, without polling.
        await _queue.Writer.WriteAsync(() =>
        {
            completion.SetResult();
            return Task.CompletedTask;
        }, linked.Token).ConfigureAwait(false);
        await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    private async Task PrefetchAsync()
    {
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Opening retries a failed attempt. Completed images remain cached.
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    public async Task<CachedArticle> GetArticleAsync(SelectionBatch batch, Post post, CancellationToken token = default)
    {
        _ = Interlocked.Increment(ref _foreground);
        try
        {
            return await StartCache(batch, post).Ready.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _ = Interlocked.Decrement(ref _foreground);
        }
    }

    private async Task CacheAsync(SelectionBatch batch, Post post, TaskCompletionSource<CachedArticle> ready, CancellationToken token)
    {
        var folder = GetArticleDirectory(batch, post.Id);
        var path = Path.Combine(folder, "body.html");
        var mapPath = Path.Combine(folder, "images.json");
        var images = new Dictionary<string, string>();
        var completions = new Dictionary<string, TaskCompletionSource<string?>>();
        var gateHeld = false;
        IPlaywright? playwright = null;
        IBrowserContext? browser = null;
        try
        {
            _ = Directory.CreateDirectory(folder);
            string? html = null;
            if (File.Exists(path))
            {
                html = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                if (File.Exists(mapPath))
                    images = JsonSerializer.Deserialize(await File.ReadAllTextAsync(mapPath, token).ConfigureAwait(false), ArticleCacheJsonContext.Default.DictionaryStringString) ?? [];
                PublishArticle();
                if (images.Values.All(File.Exists))
                {
                    await WriteAtomicAsync(Path.Combine(folder, "images.complete"), "", token).ConfigureAwait(false);
                    return;
                }
            }

            await _gate.WaitAsync(token).ConfigureAwait(false);
            gateHeld = true;
            playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            browser = await OpenBrowserAsync(playwright, batch).ConfigureAwait(false);
            if (html is null)
            {
                var page = await browser.NewPageAsync().ConfigureAwait(false);
                // Rendering needs the DOM only; resource images are downloaded by the cache below.
                _ = await page.RouteAsync("**/*", async route =>
                {
                    if (route.Request.ResourceType is "image" or "media" or "font")
                        await route.AbortAsync().ConfigureAwait(false);
                    else
                        await route.ContinueAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                await BrowserPages.NavigateAsync(page, post.Url, token).ConfigureAwait(false);
                await BrowserPages.WaitForSiteAsync(page, ".inn-singular__post__body__content", token).ConfigureAwait(false);
                await BrowserPages.EnsureLoginAsync(page, token).ConfigureAwait(false);
                html = await page.Locator(".inn-singular__post__body__content").EvaluateAsync<string>("e => e.outerHTML").ConfigureAwait(false);
                await WriteAtomicAsync(Path.Combine(folder, "original.html"), html, token).ConfigureAwait(false);
                var document = await new HtmlParser().ParseDocumentAsync(html, token).ConfigureAwait(false);
                foreach (var element in document.QuerySelectorAll("script,style,iframe,object,embed"))
                    element.Remove();
                foreach (var img in document.QuerySelectorAll("img"))
                {
                    var raw = new[] { "data-original", "data-src", "data-lazy-src", "src" }
                        .Select(img.GetAttribute).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    var url = new Uri(new Uri(post.Url), raw).AbsoluteUri;
                    var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img";
                    var imagePath = Path.Combine(folder, name);
                    images[url] = imagePath;
                    img.SetAttribute("src", new Uri(imagePath).AbsoluteUri);
                    _ = img.RemoveAttribute("srcset");
                    _ = img.RemoveAttribute("loading");
                }
                foreach (var anchor in document.QuerySelectorAll("a[href]"))
                {
                    var raw = anchor.GetAttribute("href");
                    if (Uri.TryCreate(new Uri(post.Url), raw, out var uri) && uri.Scheme is "http" or "https")
                        anchor.SetAttribute("href", uri.AbsoluteUri);
                    else
                        _ = anchor.RemoveAttribute("href");
                }
                foreach (var element in document.All)
                    foreach (var attribute in element.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToArray())
                        _ = element.RemoveAttribute(attribute.Name);
                html = document.Body!.InnerHtml;
                await WriteAtomicAsync(mapPath, JsonSerializer.Serialize(images, ArticleCacheJsonContext.Default.DictionaryStringString), token).ConfigureAwait(false);
                await WriteAtomicAsync(path, html, token).ConfigureAwait(false);
                PublishArticle();
            }

            using var fetcher = new ResourceFetcher(browser);
            foreach (var (url, imagePath) in images)
            {
                var completion = completions[imagePath];
                if (completion.Task.IsCompleted)
                    continue;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var resource = url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                        ? new Resource(url, "image/inline", Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]))
                        : await fetcher.GetAsync(url, post.Url, token).ConfigureAwait(false);
                    if (!resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || resource.Bytes.Length is 0)
                        throw new InvalidDataException("服务器未返回有效图片。");
                    await File.WriteAllBytesAsync(imagePath + ".tmp", resource.Bytes, token).ConfigureAwait(false);
                    File.Move(imagePath + ".tmp", imagePath, true);
                    _ = completion.TrySetResult(null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _ = completion.TrySetResult(ex.Message);
                }
            }
            if (images.Values.All(File.Exists))
                await WriteAtomicAsync(Path.Combine(folder, "images.complete"), "", token).ConfigureAwait(false);

            void PublishArticle()
            {
                foreach (var imagePath in images.Values.Distinct())
                {
                    var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (File.Exists(imagePath))
                        completion.SetResult(null);
                    completions[imagePath] = completion;
                }
                // Publish once. Each image completes independently without rebuilding the document.
                _ = ready.TrySetResult(new(html!, folder)
                {
                    ImageLoads = completions.ToDictionary(pair => pair.Key, pair => pair.Value.Task)
                });
                BodyCached?.Invoke(batch, post.Id);
            }
        }
        catch (Exception ex)
        {
            _ = ready.TrySetException(ex);
            foreach (var completion in completions.Values)
                _ = completion.TrySetResult(ex is OperationCanceledException ? "图片加载已取消。" : ex.Message);
        }
        finally
        {
            try
            {
                if (browser is not null)
                    await browser.DisposeAsync().ConfigureAwait(false);
                playwright?.Dispose();
            }
            finally
            {
                if (gateHeld)
                    _ = _gate.Release();
            }
        }
    }

    private static Task<IBrowserContext> OpenBrowserAsync(IPlaywright playwright, SelectionBatch batch)
    {
        var profile = Path.Combine(Path.GetDirectoryName(batch.DatabasePath)!, "browser-data");
        if (!Directory.Exists(profile))
            throw new DirectoryNotFoundException($"找不到爬虫登录会话：{profile}。请先在 HMoeWebCrawler 中登录。");
        return playwright.Chromium.LaunchPersistentContextAsync(profile, new() { Channel = "msedge", Headless = true });
    }

    public async Task<Extraction.PostResult> AcceptAsync(SelectionBatch batch, Post post, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        if (await ReadAcceptanceAsync(batch, post.Id, token).ConfigureAwait(false) is { } cached)
            return cached;
        _ = Interlocked.Increment(ref _foreground);
        try
        {
            var article = await GetArticleAsync(batch, post, token).ConfigureAwait(false);
            // Failed body illustrations must not prevent collecting the independent download page.
            _ = await article.ImagesReady.WaitAsync(token).ConfigureAwait(false);
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Another request may have completed extraction while this one waited for the browser.
                if (await ReadAcceptanceAsync(batch, post.Id, token).ConfigureAwait(false) is { } completed)
                    return completed;
                var folder = GetArticleDirectory(batch, post.Id);
                using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                await using var browser = await OpenBrowserAsync(playwright, batch).ConfigureAwait(false);
                var imageMap = Path.Combine(folder, "images.json");
                var images = File.Exists(imageMap)
                    ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(imageMap, token).ConfigureAwait(false), ArticleCacheJsonContext.Default.DictionaryStringString)
                    : null;
                using var fetcher = new ResourceFetcher(browser, images);
                using var qr = new QrReader(Path.Combine(AppContext.BaseDirectory, "models"));
                var cachedBody = await File.ReadAllTextAsync(Path.Combine(folder, "original.html"), token).ConfigureAwait(false);
                var result = await new Extractor(browser, fetcher, qr, folder).ExtractAsync(post, token, cachedBody).ConfigureAwait(false);
                await WriteAtomicAsync(Path.Combine(folder, "last-attempt.json"), JsonSerializer.Serialize(result, OutputJsonContext.Default.PostResult), token).ConfigureAwait(false);
                if (result.Status is not "ok")
                    throw new InvalidDataException(result.Errors.Count > 0 ? string.Join(Environment.NewLine, result.Errors) : "未找到下载链接，请重试或拒绝此条目。");
                await WriteAtomicAsync(Path.Combine(folder, "accepted.json"), JsonSerializer.Serialize(result, OutputJsonContext.Default.PostResult), token).ConfigureAwait(false);
                return result;
            }
            finally
            {
                _ = _gate.Release();
            }
        }
        finally
        {
            _ = Interlocked.Decrement(ref _foreground);
        }
    }

    public async Task<Extraction.PostResult?> ReadAcceptanceAsync(SelectionBatch batch, int postId, CancellationToken token = default)
    {
        var path = Path.Combine(GetArticleDirectory(batch, postId), "accepted.json");
        if (!File.Exists(path))
            return null;
        var result = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), OutputJsonContext.Default.PostResult);
        if (result is null || result.Id != postId)
            return null;
        LinkCollector.Consolidate(result);
        return result is { Status: "ok", Links.Count: > 0 } ? result : null;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken token)
    {
        await File.WriteAllTextAsync(path + ".tmp", content, token).ConfigureAwait(false);
        File.Move(path + ".tmp", path, true);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _ = _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        await Task.WhenAll(_operations.Values.Where(value => value.IsValueCreated).Select(value => value.Value.Completion)).ConfigureAwait(false);
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class ArticleCacheJsonContext : JsonSerializerContext;
