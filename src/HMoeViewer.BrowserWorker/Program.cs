using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HMoeViewer.Downloads;
using HMoeViewer.Persistence;

namespace HMoeViewer.BrowserWorker;

internal static class Program
{
    public static async Task Main()
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        using var shutdown = new CancellationTokenSource();
        using var outputGate = new SemaphoreSlim(1);
        var active = new ConcurrentDictionary<string, CancellationTokenSource>();
        var tasks = new ConcurrentDictionary<string, Task>();
        await using var articles = new ArticleReviewService();
        BaiduClientDownloadService? downloads = null;
        string? downloadProfile = null;
        articles.BodyCached += (batch, postId) => _ = NotifyBodyAsync(new("", "body", Batch: batch, PostId: postId));
        try
        {
            while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var request = JsonSerializer.Deserialize(line, BrowserWorkerJsonContext.Default.BrowserWorkerRequest)
                    ?? throw new InvalidDataException("浏览器请求为空。");
                if (request.Command is "cancel")
                {
                    if (active.TryGetValue(request.Id, out var cancellation))
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    continue;
                }
                var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                if (!active.TryAdd(request.Id, tokenSource))
                {
                    tokenSource.Dispose();
                    continue;
                }
                var task = ServeAsync(request, tokenSource);
                tasks[request.Id] = task;
                _ = task.ContinueWith(completed => tasks.TryRemove(request.Id, out _), TaskScheduler.Default);
            }
        }
        finally
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(tasks.Values).ConfigureAwait(false);
            if (downloads is not null)
                await downloads.DisposeAsync().ConfigureAwait(false);
        }

        async Task NotifyBodyAsync(BrowserWorkerResponse response)
        {
            try
            {
                await WriteAsync(response).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The owning Viewer has closed its pipe.
            }
        }

        async Task WriteAsync(BrowserWorkerResponse response)
        {
            await outputGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, BrowserWorkerJsonContext.Default.BrowserWorkerResponse)).ConfigureAwait(false);
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _ = outputGate.Release();
            }
        }

        async Task ServeAsync(BrowserWorkerRequest request, CancellationTokenSource tokenSource)
        {
            var token = tokenSource.Token;
            try
            {
                switch (request.Command)
                {
                    case "queue":
                        articles.QueueCache(request.Batch!.Value, request.Post!);
                        break;
                    case "wait":
                        await articles.WaitForQueuedCacheAsync(token).ConfigureAwait(false);
                        break;
                    case "article":
                        var article = await articles.GetArticleAsync(request.Batch!.Value, request.Post!, token).ConfigureAwait(false);
                        await WriteAsync(new(request.Id, "article", Html: article.Html, Directory: article.Directory, Images: [.. article.ImageLoads.Keys])).ConfigureAwait(false);
                        await Task.WhenAll(article.ImageLoads.Select(async pair =>
                            await WriteAsync(new(request.Id, "image", Error: await pair.Value.WaitAsync(token).ConfigureAwait(false), Image: pair.Key)).ConfigureAwait(false))).ConfigureAwait(false);
                        break;
                    case "queue-accept":
                        var queuedResult = await articles.QueueAcceptanceAsync(request.Batch!.Value, request.Post!, token).ConfigureAwait(false);
                        await WriteAsync(new(request.Id, "done", Result: queuedResult)).ConfigureAwait(false);
                        return;
                    case "accept":
                        var result = await articles.AcceptAsync(request.Batch!.Value, request.Post!, token).ConfigureAwait(false);
                        await WriteAsync(new(request.Id, "done", Result: result)).ConfigureAwait(false);
                        return;
                    case "download":
                        if (downloads is null)
                        {
                            downloadProfile = request.ProfileDirectory!;
                            downloads = new(downloadProfile);
                        }
                        if (downloadProfile != request.ProfileDirectory)
                            throw new InvalidOperationException("下载浏览器的会话目录已变化。");
                        var download = await downloads.SendAsync(request.Download!, token).ConfigureAwait(false);
                        await WriteAsync(new(request.Id, "done", Download: download)).ConfigureAwait(false);
                        return;
                    default:
                        throw new InvalidOperationException("未知浏览器操作。");
                }
                await WriteAsync(new(request.Id, "done")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteAsync(new(request.Id, "error", Error: ex.Message)).ConfigureAwait(false);
            }
            finally
            {
                _ = active.TryRemove(request.Id, out _);
                tokenSource.Dispose();
            }
        }
    }
}
