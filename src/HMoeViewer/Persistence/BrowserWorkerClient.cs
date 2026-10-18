using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HMoeViewer.Core;

namespace HMoeViewer.Persistence;

// Native AOT cannot execute Playwright's stack reflection. Keep that runtime in a private worker.
public sealed class BrowserWorkerClient : IAsyncDisposable
{
    private sealed class Pending
    {
        public TaskCompletionSource<BrowserWorkerResponse> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentDictionary<string, TaskCompletionSource<string?>> Images { get; } = new();
    }

    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private Process? _process;
    private Task? _reader;
    private Task<string>? _errors;
    private bool _disposed;

    public event Action<SelectionBatch, int>? BodyCached;

    public async Task<BrowserWorkerResponse> InvokeAsync(BrowserWorkerRequest request, CancellationToken token = default)
    {
        var pending = new Pending();
        if (!_pending.TryAdd(request.Id, pending))
            throw new InvalidOperationException("浏览器操作编号重复。");
        try
        {
            await SendAsync(request, token).ConfigureAwait(false);
            return await pending.Ready.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            _ = _articleImages.TryRemove(request.Id, out _);
            if (_pending.TryRemove(request.Id, out var abandoned))
                CompleteImages(abandoned, "图片加载已取消。");
            if (token.IsCancellationRequested)
                _ = CancelAsync(request.Id);
            throw;
        }
    }

    public async Task<CachedArticle> GetArticleAsync(BrowserWorkerRequest request, CancellationToken token)
    {
        var response = await InvokeAsync(request, token).ConfigureAwait(false);
        // The reader creates the task map before publishing the article header.
        var images = _articleImages.TryRemove(request.Id, out var tasks) ? tasks : new Dictionary<string, Task<string?>>();
        return new(response.Html!, response.Directory!) { ImageLoads = images };
    }

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Task<string?>>> _articleImages = new();

    private async Task CancelAsync(string id)
    {
        try
        {
            await SendAsync(new(id, "cancel"), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (_disposed || _process is null || _process.HasExited)
        {
        }
    }

    private async Task SendAsync(BrowserWorkerRequest request, CancellationToken token)
    {
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is null)
                Start();
            if (_process!.HasExited)
                throw new InvalidOperationException("浏览器辅助进程已退出，请重新打开 Viewer 后重试。");
            await _process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, BrowserWorkerJsonContext.Default.BrowserWorkerRequest).AsMemory(), token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    private void Start()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "browser-worker", "HMoeViewer.BrowserWorker.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("发布包缺少浏览器辅助进程。请重新发布 NativeAot 配置。", executable);
        var process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            }
        };
        try
        {
            _ = process.Start();
            _process = process;
            _errors = process.StandardError.ReadToEndAsync();
            _reader = ReadAsync(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private async Task ReadAsync(Process process)
    {
        var error = "浏览器辅助进程已退出。";
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var response = JsonSerializer.Deserialize(line, BrowserWorkerJsonContext.Default.BrowserWorkerResponse)
                    ?? throw new InvalidDataException("浏览器返回了空响应。");
                if (response is { Event: "body", Batch: { } batch })
                {
                    BodyCached?.Invoke(batch, response.PostId);
                    continue;
                }
                if (!_pending.TryGetValue(response.Id, out var pending))
                    continue;
                switch (response.Event)
                {
                    case "article":
                        foreach (var path in response.Images ?? [])
                            _ = pending.Images.TryAdd(path, new(TaskCreationOptions.RunContinuationsAsynchronously));
                        _articleImages[response.Id] = pending.Images.ToDictionary(pair => pair.Key, pair => pair.Value.Task);
                        _ = pending.Ready.TrySetResult(response);
                        break;
                    case "image":
                        if (response.Image is { } image && pending.Images.TryGetValue(image, out var completion))
                            _ = completion.TrySetResult(response.Error);
                        break;
                    case "error":
                        _ = pending.Ready.TrySetException(new InvalidOperationException(response.Error));
                        CompleteImages(pending, response.Error);
                        _ = _pending.TryRemove(response.Id, out _);
                        break;
                    case "done":
                        _ = pending.Ready.TrySetResult(response);
                        CompleteImages(pending, null);
                        _ = _pending.TryRemove(response.Id, out _);
                        break;
                }
            }
            if (_errors is { } errors && await errors.ConfigureAwait(false) is { Length: > 0 } details)
                error += Environment.NewLine + details;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            foreach (var (id, pending) in _pending)
                if (_pending.TryRemove(id, out _))
                {
                    _ = pending.Ready.TrySetException(new InvalidOperationException(error));
                    CompleteImages(pending, error);
                }
        }
    }

    private static void CompleteImages(Pending pending, string? error)
    {
        foreach (var completion in pending.Images.Values)
            _ = completion.TrySetResult(error);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_process is { } process)
        {
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Only the private worker and its browsers belong to this lifetime.
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            if (_reader is { } reader)
                await reader.ConfigureAwait(false);
            process.Dispose();
        }
        _writeGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
