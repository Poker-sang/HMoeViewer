using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace HMoeViewer.Extraction;

public static class BrowserPages
{
    public static async Task NavigateAsync(IPage page, string url, CancellationToken token = default)
    {
        await PageCooldown.Shared.WaitAsync(token).ConfigureAwait(false);
        _ = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 })
            .WaitAsync(token).ConfigureAwait(false);
    }

    public static async Task WaitForSiteAsync(IPage page, string selector, CancellationToken token = default)
    {
        _ = await page.WaitForFunctionAsync("() => location.href !== 'about:blank'", null, new() { Timeout = 20000 })
            .WaitAsync(token).ConfigureAwait(false);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).WaitAsync(token).ConfigureAwait(false);
        // The site's simple slide-to-unlock form occasionally precedes the download page.
        // Use its normal mouse interaction; a different challenge is reported for manual handling.
        if (page.Url.Contains("/WAF/", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await page.Locator("#handler").BoundingBoxAsync().WaitAsync(token).ConfigureAwait(false);
            var track = await page.Locator("#input").BoundingBoxAsync().WaitAsync(token).ConfigureAwait(false);
            if (handle is null || track is null)
                throw new InvalidOperationException("网站验证需要人工处理，请使用 --headed 完成验证后重试。");
            await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2).ConfigureAwait(false);
            await page.Mouse.DownAsync().ConfigureAwait(false);
            await page.Mouse.MoveAsync(track.X + track.Width - 5, handle.Y + handle.Height / 2, new() { Steps = 30 }).ConfigureAwait(false);
            await PageCooldown.Shared.WaitAsync(token).ConfigureAwait(false);
            await page.Mouse.UpAsync().ConfigureAwait(false);
            await page.WaitForURLAsync(url => !url.Contains("/WAF/"), new() { Timeout = 20000 }).WaitAsync(token).ConfigureAwait(false);
        }

        try
        {
            await page.Locator(selector).First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 25000 })
                .WaitAsync(token).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException($"页面未出现 {selector}: {page.Url.Split('#')[0]}（{await page.TitleAsync().ConfigureAwait(false)}）", ex);
        }
    }

    public static async Task EnsureLoginAsync(IPage page, CancellationToken token = default)
    {
        var loggedIn = await page.EvaluateAsync<bool>(
            """
            async () => {
                const response = await fetch('/wp-admin/admin-ajax.php?action=285d6af5ed069e78e04b2d054182dcb5&d6ca819426678dab7a26ecb2802d8aec%5Btype%5D=checkUnread&6f05c9bced69c22452fcd115e6fc4838%5Btype%5D=getHomepagePosts');
                if (!response.ok) return false;
                const data = await response.json();
                return Number(data.user?.id ?? 0) > 0;
            }
            """).WaitAsync(token).ConfigureAwait(false);
        if (!loggedIn)
            throw new InvalidOperationException("爬虫会话已失效；请在 HMoeWebCrawler 中重新登录后重试。");
    }
}
