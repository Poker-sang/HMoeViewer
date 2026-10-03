using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace HMoeViewer.Extraction;

public sealed record Resource(string Url, string ContentType, byte[] Bytes);

public sealed class ResourceFetcher(IBrowserContext browser, IReadOnlyDictionary<string, string>? cachedImages = null) : IDisposable
{
    private const int MaxBytes = 30 * 1024 * 1024;
    private readonly HashSet<string> _blockedBrowserHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All
    })
    { Timeout = TimeSpan.FromSeconds(25) };

    public async Task<Resource> GetAsync(string url, string source, CancellationToken token = default)
    {
        if (cachedImages?.TryGetValue(url, out var path) is true && File.Exists(path))
            return new(url, "image/cached", await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
        if (!LinkRules.IsImage(url))
            await PageCooldown.Shared.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await GetHttpAsync(url, source, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden && !_blockedBrowserHosts.Contains(new Uri(url).Host))
        {
            try
            {
                return await GetBrowserImageAsync(url, token).ConfigureAwait(false);
            }
            catch (Exception browserError) when (browserError is not OperationCanceledException)
            {
                _ = _blockedBrowserHosts.Add(new Uri(url).Host);
                throw new HttpRequestException("图床返回 HTTP 403，浏览器验证后仍未取得图片。", browserError, HttpStatusCode.Forbidden);
            }
        }
    }

    private async Task<Resource> GetHttpAsync(string url, string source, CancellationToken token)
    {
        for (var redirects = 0; redirects < 8; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
            request.Headers.Referrer = new Uri(source);
            var cookies = await browser.CookiesAsync([url]).WaitAsync(token).ConfigureAwait(false);
            if (cookies.Count > 0)
                _ = request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect && response.Headers.Location is { } location)
            {
                url = new Uri(new Uri(url), location).AbsoluteUri;
                continue;
            }
            _ = response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            // Only fetch page/image bodies. An unfamiliar file-download endpoint is recorded by URL.
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return new Resource(url, contentType, []);
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new InvalidDataException("资源超过 30 MiB 限制。");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[65536];
            int read;
            while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                    throw new InvalidDataException("资源超过 30 MiB 限制。");
                buffer.Write(chunk, 0, read);
            }
            return new Resource(url, contentType, buffer.ToArray());
        }
        throw new HttpRequestException("重定向超过 8 次。");
    }

    private async Task<Resource> GetBrowserImageAsync(string url, CancellationToken token)
    {
        var page = await browser.NewPageAsync().ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            page.Response += (_, response) =>
            {
                if (response.Ok && response.Headers.GetValueOrDefault("content-type", "").StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    && response.Request.IsNavigationRequest)
                    _ = completion.TrySetResult(response);
            };
            _ = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 })
                .WaitAsync(token).ConfigureAwait(false);
            var image = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            var bytes = await image.BodyAsync().WaitAsync(token).ConfigureAwait(false);
            if (bytes.Length > MaxBytes)
                throw new InvalidDataException("资源超过 30 MiB 限制。");
            return new Resource(image.Url, image.Headers.GetValueOrDefault("content-type", ""), bytes);
        }
        finally
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
