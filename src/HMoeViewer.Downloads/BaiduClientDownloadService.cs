using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace HMoeViewer.Downloads;

/// <summary>通过分享页的官方下载 SDK 发送任务；不依赖 Viewer 或 Avalonia。</summary>
public sealed class BaiduClientDownloadService(string profileDirectory) : IClientDownloadService
{
    private readonly SemaphoreSlim _gate = new(1);
    private IPlaywright? _playwright;
    private bool _dispatchStarted;

    public static bool Supports(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" && uri.Host.Equals("pan.baidu.com", StringComparison.OrdinalIgnoreCase)
        && (uri.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal) || uri.AbsolutePath is "/share/init");

    public static bool IsDownloadProtocol(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "baiduyunguanjia" && uri.Host is "evoked-download"
        && HasQueryValue(uri, "browserId") && HasQueryValue(uri, "seq");

    private static bool HasQueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&').Any(part =>
        part.StartsWith(name + "=", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(Uri.UnescapeDataString(part[(name.Length + 1)..])));

    public async Task<ClientDownloadResult> SendAsync(ClientDownloadRequest request, CancellationToken token = default)
    {
        if (!Supports(request.Url) || !OperatingSystem.IsWindows())
            return new(ClientDownloadStatus.Unsupported, "目前仅支持 Windows 百度网盘分享链接。");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        using var evaluationLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            _dispatchStarted = false;
            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            // 每次发送拥有独立的无头浏览器；所有返回路径都会关闭它，登录数据仍保留在目录中。
            await using var context = await _playwright.Chromium.LaunchPersistentContextAsync(profileDirectory,
                new() { Channel = "msedge", Headless = true }).ConfigureAwait(false);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync().ConfigureAwait(false);
            _ = await context.ExposeBindingAsync("hmoeLaunchClient", (BindingSource source, string url) =>
            {
                if (!ReferenceEquals(source.Page, page) || !Supports(source.Page.Url) || !IsDownloadProtocol(url))
                    throw new InvalidOperationException("官方 SDK 返回了无效的客户端下载协议。");
                _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }).ConfigureAwait(false);
            _ = await context.ExposeBindingAsync("hmoeDownloadStarted", source =>
            {
                if (ReferenceEquals(source.Page, page))
                    _dispatchStarted = true;
            }).ConfigureAwait(false);
            await using var cancellation = token.Register(() => _ = page.CloseAsync());
            _ = await page.GotoAsync(request.Url, new() { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(request.ExtractionCode))
                await PrepareShareAsync(page, request.ExtractionCode, token).ConfigureAwait(false);
            await using var scriptStream = typeof(BaiduClientDownloadService).Assembly.GetManifestResourceStream("HMoeViewer.Downloads.BaiduDownload.js")!;
            using var reader = new StreamReader(scriptStream);
            var script = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            var result = await EvaluateDownloadAsync(page, script, () => _dispatchStarted, evaluationLifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(90), token).ConfigureAwait(false);
            var state = result.GetProperty("status").GetString();
            var message = result.GetProperty("message").GetString()!;
            return new(state switch
            {
                "submitted" => ClientDownloadStatus.Submitted,
                "interaction" => ClientDownloadStatus.NeedsInteraction,
                "verification" => ClientDownloadStatus.NeedsVerification,
                _ => ClientDownloadStatus.Failed
            }, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await evaluationLifetime.CancelAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(ClientDownloadStatus.Failed, ex is TimeoutException
                ? "未收到客户端回执，不能确认是否送达；请先检查客户端任务列表，避免重复发送。"
                : _dispatchStarted && ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
                    ? "页面跳转中断了发送，未能确认客户端是否接收；请先检查客户端任务列表，避免重复发送。"
                    : $"发送到客户端失败：{ex.Message}");
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal static async Task<JsonElement> EvaluateDownloadAsync(IPage page, string script, Func<bool> hasDispatchStarted, CancellationToken token = default)
    {
        for (var attempt = 0; ; ++attempt)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await page.EvaluateAsync<JsonElement>(script).WaitAsync(token).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (!token.IsCancellationRequested && !hasDispatchStarted() && attempt < 3
                && ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase))
            {
                // 提取码提交、登录等导航会销毁上下文；只有尚未开始发送才可重试。
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).ConfigureAwait(false);
            }
        }
    }

    internal static async Task PrepareShareAsync(IPage page, string extractionCode, CancellationToken token = default)
    {
        // 提取码输入框由 SPA 异步渲染，DOMContentLoaded 时通常还不存在。
        for (var attempt = 0; attempt < 40; ++attempt)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var input = page.Locator("input[placeholder*='提取码']:visible, input#accessCode:visible");
                if (await input.CountAsync().ConfigureAwait(false) is 1)
                {
                    await input.FillAsync(extractionCode).ConfigureAwait(false);
                    var submit = page.GetByRole(AriaRole.Button, new() { Name = "提取文件", Exact = true });
                    if (await submit.CountAsync().ConfigureAwait(false) is 1 && await submit.IsVisibleAsync().ConfigureAwait(false))
                        await submit.ClickAsync().ConfigureAwait(false);
                    else
                        await input.PressAsync("Enter").ConfigureAwait(false);
                    return;
                }
                if (await page.EvaluateAsync<bool>("() => { const files = window.locals?.get?.('file_list') ?? window.locals?.file_list ?? window.yunData?.file_list; return Array.isArray(files) && files.length > 0; }").ConfigureAwait(false))
                    return;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase))
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).ConfigureAwait(false);
            }
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            _ = _gate.Release();
        }
        GC.SuppressFinalize(this);
    }
}
