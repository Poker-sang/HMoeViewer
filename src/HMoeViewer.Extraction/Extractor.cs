using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Microsoft.Playwright;

namespace HMoeViewer.Extraction;

public sealed class Extractor(IBrowserContext browser, ResourceFetcher fetcher, QrReader qr, string output)
{
    private const string ContentSelector = ".inn-singular__post__body__content";
    private const string DownloadSelector = "#inn-download-page__content";
    private readonly HtmlParser _parser = new();
    private readonly Dictionary<string, string[]> _imageCache = new(StringComparer.Ordinal);
    private bool _loginChecked;
    private readonly HashSet<string> _visitedDownloadPages = new(StringComparer.Ordinal);

    public async Task<PostResult> ExtractAsync(Post post, CancellationToken token = default, string? cachedBody = null)
    {
        var result = new PostResult(post.Id, post.Title, post.Url, "pending", [], [], [], [], DateTimeOffset.UtcNow, LinkCollector.FormatVersion);
        _visitedDownloadPages.Clear();
        var existingPages = browser.Pages.ToHashSet();
        var bodyImageErrors = new HashSet<string>(StringComparer.Ordinal);
        Func<IRoute, Task> captureDownload = route => route.Request switch
        {
            { IsNavigationRequest: true } request when LinkRules.IsDownload(request.Url) =>
                route.FulfillAsync(new() { ContentType = "text/html", Body = "<html><body></body></html>" }),
            { ResourceType: "image" or "media" or "font" } => route.AbortAsync(),
            _ => route.FallbackAsync()
        };
        try
        {
            // A known download destination is already the result. Commit its URL locally so a slow or
            // unreachable provider cannot hide a JavaScript popup's address behind a navigation timeout.
            // Rendering images is unnecessary here; QR bytes are fetched separately by ResourceFetcher.
            await browser.RouteAsync("**/*", captureDownload).ConfigureAwait(false);
            if (cachedBody is not null)
            {
                await ScanHtmlAsync(cachedBody, post.Url, ContentSelector, result, null, token).ConfigureAwait(false);
                RememberBodyImageErrors();
            }
            if (!result.Links.Any(link => LinkRules.IsDownload(link.Url)))
            {
                // A live article is needed only to operate JavaScript download entrances or obtain uncached content.
                var page = await browser.NewPageAsync().ConfigureAwait(false);
                await BrowserPages.NavigateAsync(page, post.Url, token).ConfigureAwait(false);
                await BrowserPages.WaitForSiteAsync(page, ContentSelector, token).ConfigureAwait(false);
                if (!_loginChecked)
                {
                    await BrowserPages.EnsureLoginAsync(page, token).ConfigureAwait(false);
                    _loginChecked = true;
                }
                if (cachedBody is null)
                {
                    await ScanHtmlAsync(await page.ContentAsync().ConfigureAwait(false), page.Url, ContentSelector, result, null, token).ConfigureAwait(false);
                    RememberBodyImageErrors();
                }
                // Scan all body links and QR images before falling back to download pages.
                if (!result.Links.Any(link => LinkRules.IsDownload(link.Url)))
                {
                    await FollowEntriesAsync(page, page.Locator(ContentSelector), result, null, 0, token).ConfigureAwait(false);
                    await page.Locator("#inn-singular__post__toolbar a").First.WaitForAsync(new() { Timeout = 20000 })
                        .WaitAsync(token).ConfigureAwait(false);
                    await FollowEntriesAsync(page, page.Locator("#inn-singular__post__toolbar"), result, null, 0, token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex is OperationCanceledException && token.IsCancellationRequested ? "操作已取消，可续跑。" : ShortError(ex));
        }
        finally
        {
            await browser.UnrouteAsync("**/*", captureDownload).ConfigureAwait(false);
            foreach (var page in browser.Pages.Where(page => !existingPages.Contains(page)).ToArray())
                await page.CloseAsync().ConfigureAwait(false);
        }

        LinkCollector.Consolidate(result);
        // Keep body image failures as diagnostics, but a successfully parsed fallback is usable.
        var failed = result.Errors.Any(error => result.Links.Count is 0 || !bodyImageErrors.Contains(error));
        return result with { Status = failed ? "partial" : result.Links.Count > 0 ? "ok" : "no-links" };

        void RememberBodyImageErrors()
        {
            foreach (var image in result.Images.Where(image => image.DownloadItemIndex is null && image.Error is not null))
                _ = bodyImageErrors.Add($"图片 {image.Url}: {image.Error}");
        }
    }

    private async Task ReadDownloadPageAsync(IPage page, PostResult result, int depth, CancellationToken token)
    {
        await BrowserPages.WaitForSiteAsync(page, DownloadSelector + " fieldset", token).ConfigureAwait(false);
        if (!_visitedDownloadPages.Add(page.Url))
            return;
        var rows = page.Locator(DownloadSelector + " fieldset");
        var count = await rows.CountAsync().ConfigureAwait(false);
        var source = page.Url.Split('#')[0];
        var firstIndex = result.Downloads.Select(item => item.Index).DefaultIfEmpty(0).Max() + 1;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var row = rows.Nth(i);
                var label = LinkRules.Compact(await row.Locator("legend").InnerTextAsync().ConfigureAwait(false));
                // Values are live DOM properties; InnerText/HTML alone can omit both password fields.
                var extractionCode = await ReadInputAsync(row, ".inn-download-page__content__item__download-pwd input").ConfigureAwait(false);
                var archivePassword = await ReadInputAsync(row, ".inn-download-page__content__item__extract-pwd input").ConfigureAwait(false);
                var item = new DownloadItem(firstIndex + i, label, extractionCode, archivePassword, []);
                result.Downloads.Add(item);
                var html = await row.EvaluateAsync<string>("element => element.outerHTML").ConfigureAwait(false);
                await ScanHtmlAsync(html, source, "fieldset", result, item, token).ConfigureAwait(false);
                await FollowEntriesAsync(page, row, result, item, depth, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Errors.Add($"下载项 {firstIndex + i}: {ShortError(ex)}");
            }
        }
    }

    private static async Task<string?> ReadInputAsync(ILocator row, string selector)
    {
        var input = row.Locator(selector);
        if (await input.CountAsync().ConfigureAwait(false) is 0)
            return null;
        var value = await input.First.InputValueAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task FollowEntriesAsync(IPage page, ILocator scope, PostResult result, DownloadItem? item, int depth, CancellationToken token)
    {
        if (depth > 5)
            throw new InvalidDataException("下载入口跳转超过 5 层，已停止该分支。");
        var entries = scope.Locator("a, button");
        var count = await entries.CountAsync().ConfigureAwait(false);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var originalPageUrl = page.Url;
            IPage? target = null;
            string? url = null;
            try
            {
                var entry = entries.Nth(i);
                var raw = await entry.GetAttributeAsync("href").ConfigureAwait(false);
                var icon = await entry.Locator(".fa-cloud-download-alt").CountAsync().ConfigureAwait(false) > 0;
                if (await entry.EvaluateAsync<bool>("e => !!e.closest('#inn-toc')").ConfigureAwait(false)
                    || (raw?.StartsWith('#') is true && !icon && await entry.GetAttributeAsync("onclick").ConfigureAwait(false) is null))
                    continue;
                url = LinkRules.Resolve(raw, page.Url);
                if (!icon && !LinkRules.DownloadLabel(await entry.InnerTextAsync().ConfigureAwait(false))
                    && !(url is not null && new Uri(url).AbsolutePath.StartsWith("/download", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (url is not null && (LinkRules.IsDownload(url) || LinkRules.IsImage(url)))
                {
                    item?.Targets.Add(url);
                    await HandleTargetAsync(url, originalPageUrl.Split('#')[0], result, item, token).ConfigureAwait(false);
                    continue;
                }
                if (url is not null)
                {
                    target = await browser.NewPageAsync().ConfigureAwait(false);
                    await BrowserPages.NavigateAsync(target, url, token).ConfigureAwait(false);
                }
                else
                    target = await ClickEntryAsync(page, entry, token).ConfigureAwait(false);
                // Poll the live popup location: its initial navigation may have committed before the
                // Popup event arrives, so waiting for another navigation can miss the address.
                _ = await target.WaitForFunctionAsync("() => location.href !== 'about:blank'", null, new() { Timeout = 20000 })
                    .WaitAsync(token).ConfigureAwait(false);
                if (!LinkRules.IsValidTarget(target.Url))
                    throw new InvalidDataException("下载入口跳转到了浏览器错误页或无效地址。");
                item?.Targets.Add(target.Url);
                if (LinkRules.IsDownload(target.Url) || LinkRules.IsImage(target.Url))
                    await HandleTargetAsync(target.Url, originalPageUrl.Split('#')[0], result, item, token).ConfigureAwait(false);
                else
                {
                    await target.WaitForLoadStateAsync(LoadState.DOMContentLoaded).WaitAsync(token).ConfigureAwait(false);
                    if (new Uri(target.Url).AbsolutePath.StartsWith("/download", StringComparison.OrdinalIgnoreCase)
                        || target.Url.Contains("/WAF/", StringComparison.OrdinalIgnoreCase)
                        || await target.Locator(DownloadSelector).CountAsync().ConfigureAwait(false) > 0)
                        await ReadDownloadPageAsync(target, result, depth + 1, token).ConfigureAwait(false);
                    else
                    {
                        await ScanHtmlAsync(await target.ContentAsync().ConfigureAwait(false), target.Url, "body", result, item, token).ConfigureAwait(false);
                        await FollowEntriesAsync(target, target.Locator("body"), result, item, depth + 1, token).ConfigureAwait(false);
                        if (!result.Links.Any(link => link.Origins.Any(origin => origin.Source == target.Url)))
                            AddLink(result, target.Url, originalPageUrl.Split('#')[0], "candidate", item);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Errors.Add($"入口 {i + 1} ({originalPageUrl.Split('#')[0]}): {ShortError(ex)}");
                if (url is not null && (target is null || LinkRules.IsValidTarget(target.Url)))
                    AddLink(result, url, originalPageUrl.Split('#')[0], "unresolved", item);
            }
            finally
            {
                if (target is not null && target != page)
                    await target.CloseAsync().ConfigureAwait(false);
                if (page.Url != originalPageUrl)
                    await BrowserPages.NavigateAsync(page, originalPageUrl, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IPage> ClickEntryAsync(IPage page, ILocator entry, CancellationToken token)
    {
        var original = page.Url;
        var completion = new TaskCompletionSource<IPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Popup(object? sender, IPage popup) => completion.TrySetResult(popup);
        void Navigated(object? sender, IFrame frame)
        {
            if (frame == page.MainFrame && frame.Url != original)
                _ = completion.TrySetResult(page);
        }
        page.Popup += Popup;
        page.FrameNavigated += Navigated;
        try
        {
            await PageCooldown.Shared.WaitAsync(token).ConfigureAwait(false);
            await entry.ClickAsync(new() { Timeout = 15000 }).WaitAsync(token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        }
        finally
        {
            page.Popup -= Popup;
            page.FrameNavigated -= Navigated;
        }
    }

    private async Task ScanHtmlAsync(string html, string source, string selector, PostResult result, DownloadItem? item, CancellationToken token)
    {
        var document = await _parser.ParseDocumentAsync(html, token).ConfigureAwait(false);
        var root = document.QuerySelector(selector) ?? throw new InvalidDataException($"页面缺少正文区域: {source}");
        foreach (var unwanted in root.QuerySelectorAll("script, style, #inn-toc"))
            unwanted.Remove();
        var images = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in root.QuerySelectorAll("img"))
        {
            foreach (var attribute in new[] { "src", "data-src", "data-original", "data-lazy-src" })
            {
                var raw = image.GetAttribute(attribute);
                if (raw?.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) is true)
                    _ = images.Add(raw);
                else if (LinkRules.Resolve(raw, source) is { } url)
                    _ = images.Add(url);
            }
            foreach (var attribute in new[] { "srcset", "data-srcset" })
                foreach (var candidate in (image.GetAttribute(attribute) ?? "").Split(','))
                    if (LinkRules.Resolve(candidate.Trim().Split(' ')[0], source) is { } url)
                        _ = images.Add(url);
            if (LinkRules.Resolve(image.Closest("a")?.GetAttribute("href"), source) is { } original && LinkRules.IsImage(original))
                _ = images.Add(original);
        }

        foreach (var anchor in root.QuerySelectorAll("a[href]"))
        {
            var url = LinkRules.Resolve(anchor.GetAttribute("href"), source);
            if (url is null)
                continue;
            if (LinkRules.IsImage(url))
                _ = images.Add(url);
            else if (LinkRules.IsDownload(url))
                AddLink(result, url, source, "download", item);
        }
        // Article passwords and prose are deliberately not parsed or stored.
        foreach (var url in LinkRules.FindUrls(root.TextContent))
            if (Uri.TryCreate(url, UriKind.Absolute, out _) && LinkRules.IsDownload(url))
                AddLink(result, url, source, "download", item);
        foreach (var image in images)
            await ScanImageAsync(image, source, result, item, token).ConfigureAwait(false);
    }

    private async Task HandleTargetAsync(string url, string source, PostResult result, DownloadItem? item, CancellationToken token)
    {
        if (LinkRules.IsImage(url))
        {
            await ScanImageAsync(url, source, result, item, token).ConfigureAwait(false);
            return;
        }
        if (LinkRules.IsDownload(url))
        {
            AddLink(result, url, source, "download", item);
            return;
        }
        AddLink(result, url, source, "candidate", item);
        var response = await fetcher.GetAsync(url, source, token).ConfigureAwait(false);
        if (response.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            DecodeImage(url, source, response.Bytes, result, item, token);
            _ = result.Links.RemoveAll(link => link.Url == url && link.Origins.All(origin => origin.Kind == "candidate" && origin.DownloadItemIndex == item?.Index));
        }
        else if (response.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            await ScanHtmlAsync(System.Text.Encoding.UTF8.GetString(response.Bytes), response.Url, "body", result, item, token).ConfigureAwait(false);
    }

    private async Task ScanImageAsync(string url, string source, PostResult result, DownloadItem? item, CancellationToken token)
    {
        if (result.Images.Any(image => image.Url == url && image.DownloadItemIndex == item?.Index))
            return;
        if (_imageCache.TryGetValue(url, out var payloads))
        {
            RecordImage(url, source, payloads, null, result, item);
            return;
        }
        try
        {
            var bytes = url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(url[(url.IndexOf(',') + 1)..])
                : (await fetcher.GetAsync(url, source, token).ConfigureAwait(false)).Bytes;
            DecodeImage(url, source, bytes, result, item, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordImage(url, source, [], ShortError(ex), result, item);
        }
    }

    private void DecodeImage(string url, string source, byte[] bytes, PostResult result, DownloadItem? item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var payloads = qr.Decode(bytes);
        _imageCache[url] = payloads;
        RecordImage(url, source, payloads, null, result, item);
        if (payloads.Length > 0)
        {
            var directory = Path.Combine(output, "qr-images");
            _ = Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            File.WriteAllBytes(Path.Combine(directory, hash + ".img"), bytes);
        }
    }

    private static void RecordImage(string url, string source, string[] payloads, string? error, PostResult result, DownloadItem? item)
    {
        result.Images.Add(new ImageScan(url, source, item?.Index, payloads, error));
        if (error is not null)
            result.Errors.Add($"图片 {url}: {error}");
        foreach (var payload in payloads)
            foreach (var link in LinkRules.FindUrls(payload))
                if (Uri.TryCreate(link, UriKind.Absolute, out _))
                    AddLink(result, link, url, LinkRules.IsDownload(link) ? "qr-download" : "qr-candidate", item);
        if (item is not null && error is null && payloads.Length is 0)
            result.Errors.Add($"下载项 {item.Index} 的图片未识别到二维码: {url}");
    }

    private static void AddLink(PostResult result, string url, string source, string kind, DownloadItem? item)
    {
        LinkCollector.Add(result, url, source, kind, item);
    }

    private static string ShortError(Exception ex) => ex.Message.Split('\n')[0].Trim();
}
