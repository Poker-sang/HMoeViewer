using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HMoeData;
using HMoeData.Models;
using Microsoft.Playwright;
using Cookie = System.Net.Cookie;

namespace HMoeWebCrawler;

public class HMoeSession : IAsyncDisposable
{
    public const string Domain = "https://www.mhh1.com/";

    private const string HomepageAction = "285d6af5ed069e78e04b2d054182dcb5";
    private const string CaptchaAction = "b9215121b88d889ea28808c5adabbbf5";
    private const string LoginAction = "0ac2206cd584f32fba03df08b4123264";
    private const string SignAction = "9f9fa05823795c1c74e8c27e8d5e6930";
    private const string SuperSearchAction = "b9338a11fcc41c1ed5447625d1c0e743";

    /// <summary>
    /// 最大请求间隔，超过后中断
    /// </summary>
    public TimeSpan CoolDownThreshold { get; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 每次请求时间间隔
    /// </summary>
    public TimeSpan DefaultCoolDown = TimeSpan.FromSeconds(3);

    private readonly List<Task> _imageDownloadTasks = [];

    private IPlaywright? _playwright;
    private IBrowserContext? _browserContext;
    private IPage? _page;
    private readonly SemaphoreSlim _downloadSemaphore = new(4);
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer = new();
    private string? _loginNonce;
    private DateTime _lastRequest = DateTime.MinValue;

    public HMoeSession()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookieContainer
        });
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
    }

    public IReadOnlyList<Task> ImageDownloadTasks => _imageDownloadTasks;

    public async Task InitAsync()
    {
        _playwright = await Playwright.CreateAsync();

        var userDataDir = Path.Combine(
#if DEBUG
            @"D:\HMoeWebCrawler",
#else
            Environment.CurrentDirectory,
#endif
            "browser-data");

        Directory.CreateDirectory(userDataDir);

        _browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(
            userDataDir,
            new()
            {
                Headless = false,
                Channel = "msedge",
                ViewportSize = new() { Width = 1280, Height = 800 },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0"
            });

        _page = _browserContext.Pages.FirstOrDefault() ?? await _browserContext.NewPageAsync();

        // 隐藏 webdriver 标识以避免被检测
        await _page.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");
    }

    public async Task NavigateToSiteAsync()
    {
        ConsoleLogger.Info("正在打开网站...");
        var response = await _page!.GotoAsync(Domain, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

        // 等待5秒挑战或其他加载完成
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
        }
        catch (TimeoutException)
        {
            ConsoleLogger.Warning("等待页面空闲超时，继续处理");
        }

        // 如果页面标题包含 challenge 关键字，等待跳转完成
        var title = await _page.TitleAsync();
        if (title.Contains("moment", StringComparison.OrdinalIgnoreCase)
            || title.Contains("check", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleLogger.Info("检测到验证页面，等待自动跳转...");
            try
            {
                await _page.WaitForURLAsync($"{Domain}**", new() { Timeout = 30000 });
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
            }
            catch (TimeoutException)
            {
                ConsoleLogger.Warning("验证页面等待超时，继续处理");
            }
        }

        ConsoleLogger.Success("网站已打开: " + _page.Url);
        await SyncCookiesToHttpClientAsync();
    }

    public async Task EnsureLoggedInAsync(string email, string password)
    {
        // The login cookie can remain in the profile after it has expired. The
        // homepage nonce response reflects the server-side session state.
        var (nonce, isLoggedIn) = await FetchNonceInfoFromPageAsync();

        if (isLoggedIn)
        {
            ConsoleLogger.Success("当前会话已登录");
            return;
        }

        ConsoleLogger.Info("当前会话未登录，开始登录...");

        // 获取验证码
        var captchaJson = await PageFetchAsync(
            $"/wp-admin/admin-ajax.php?_nonce={nonce}&action={CaptchaAction}&type=getCaptcha");

        ConsoleLogger.Info("验证码已获取，请在浏览器窗口中查看");

        var captchaResponse = JsonSerializer.Deserialize(captchaJson, HMoeDataJsonContext.Default.ApiResponse)
                              ?? throw new InvalidOperationException("Failed to deserialize captcha response.");
        var captchaData = captchaResponse.GetData(HMoeDataJsonContext.Default.ImageDataResult);
        var base64 = captchaData.ImgData;

        // 在浏览器中显示验证码
        await _page!.EvaluateAsync(
            """
            (imgSrc) => {
                const div = document.createElement('div');
                div.id = 'captcha-overlay';
                div.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.8);z-index:99999;display:flex;align-items:center;justify-content:center;flex-direction:column;';
                const img = document.createElement('img');
                img.src = imgSrc;
                img.style.cssText = 'max-width:400px;background:white;padding:10px;border-radius:8px;';
                div.appendChild(img);
                const text = document.createElement('p');
                text.textContent = '请在控制台输入验证码';
                text.style.cssText = 'color:white;font-size:20px;margin-top:20px;';
                div.appendChild(text);
                document.body.appendChild(div);
            }
            """, base64);

        string? captcha;
        do
        {
            ConsoleLogger.Prompt("请输入验证码: ");
            captcha = Console.ReadLine();
        } while (string.IsNullOrWhiteSpace(captcha));

        await _page.EvaluateAsync("() => document.getElementById('captcha-overlay')?.remove()");

        // 提交登录（使用参数化调用防止注入）
        var loginJson = await _page.EvaluateAsync<string>(
            """
            async ([nonce, email, pwd, captcha, loginAction]) => {
                const url = `/wp-admin/admin-ajax.php?_nonce=${nonce}&action=${loginAction}&type=login`;
                const formData = new URLSearchParams();
                formData.append('email', email);
                formData.append('pwd', pwd);
                formData.append('captcha', captcha);
                formData.append('type', 'login');
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/x-www-form-urlencoded' },
                    body: formData.toString()
                });
                return await response.text();
            }
            """, new object[] { nonce, email, password, captcha, LoginAction });

        ConsoleLogger.Info("登录响应: " + loginJson);

        using (var loginDocument = JsonDocument.Parse(loginJson))
        {
            var root = loginDocument.RootElement;
            if (root.TryGetProperty("code", out var code)
                && code.ValueKind is JsonValueKind.Number
                && code.GetInt32() is not 0)
            {
                var message = root.TryGetProperty("msg", out var msg)
                    ? msg.GetString()
                    : "未知错误";
                throw new InvalidOperationException($"登录失败 ({code.GetInt32()}): {message}");
            }
        }

        ConsoleLogger.Info("登录请求已接受，正在确认会话状态...");

        await _page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await SyncCookiesToHttpClientAsync();

        var loggedInInfo = await FetchNonceInfoFromPageAsync();
        if (!loggedInInfo.IsLoggedIn)
            throw new InvalidOperationException("登录响应成功，但服务器仍未识别为已登录。");

        ConsoleLogger.Success("登录完成");
    }

    public async Task<string> FetchNonceAsync()
    {
        var nonce = await FetchNonceFromPageAsync();
        _loginNonce = nonce;
        _ = await SignAsync(nonce);
        return nonce;
    }

    private async Task<string> FetchNonceFromPageAsync()
    {
        return (await FetchNonceInfoFromPageAsync()).Nonce;
    }

    private async Task<(string Nonce, bool IsLoggedIn)> FetchNonceInfoFromPageAsync()
    {
        ConsoleLogger.Info("正在获取会话凭据...");
        var nonceJson = await PageFetchAsync(
            $"/wp-admin/admin-ajax.php?action={HomepageAction}&d6ca819426678dab7a26ecb2802d8aec%5Btype%5D=checkUnread&6f05c9bced69c22452fcd115e6fc4838%5Btype%5D=getHomepagePosts");

        using var jsonDocument = JsonDocument.Parse(nonceJson);
        var root = jsonDocument.RootElement;
        var nonce = root.GetProperty("_nonce").GetString()
                    ?? throw new InvalidOperationException("Nonce not found in response.");

        var isLoggedIn = root.TryGetProperty("user", out var user)
                         && user.ValueKind is JsonValueKind.Object
                         && user.TryGetProperty("id", out var userId)
                         && IsAuthenticatedUserId(userId);

        ConsoleLogger.Success("获取 nonce: " + nonce);
        return (nonce, isLoggedIn);
    }

    private static bool IsAuthenticatedUserId(JsonElement userId)
    {
        if (userId.ValueKind == JsonValueKind.Number)
            return userId.TryGetInt64(out var numericId) && numericId is not 0;

        return userId.ValueKind == JsonValueKind.String
               && long.TryParse(userId.GetString(), out var stringId)
               && stringId is not 0;
    }

    public async Task<bool> SignAsync(string nonce)
    {
        ConsoleLogger.Info("正在签到...");
        var signJson = await PageFetchAsync(
            $"/wp-admin/admin-ajax.php?_nonce={nonce}&action={SignAction}&type=goSign");

        var response = JsonSerializer.Deserialize(signJson, HMoeDataJsonContext.Default.ApiResponse)
                       ?? throw new InvalidOperationException("Failed to deserialize sign response.");
        var status = response.Code is 0;
        if (status)
            ConsoleLogger.Success("签到成功: " + response.Message);
        else
            ConsoleLogger.Warning("签到失败: " + response.Message);
        return status;
    }

    public async Task<Stack<Post>> SearchPageAsync(SearchData data)
    {
        var coolDown = DefaultCoolDown;

        while (true)
            try
            {
                if (_loginNonce is not null)
                {
                    while (DateTime.UtcNow < _lastRequest + coolDown)
                        await Task.Delay(500);

                    var query = data.Encode();
                    ConsoleLogger.Info("正在下载第 " + data.Paged + " 页...");

                    var searchJson = await PageFetchAsync(
                        $"/wp-admin/admin-ajax.php?_nonce={_loginNonce}&action={SuperSearchAction}&query={query}");

                    var result = JsonSerializer.Deserialize(searchJson, HMoeDataJsonContext.DefaultOverride.ApiResponse)
                                 ?? throw new InvalidOperationException("Failed to deserialize search response.");

                    if (result.Code is not 10007)
                    {
                        var r = result.GetData(HMoeDataJsonContext.Default.PostsSearchResult);
                        ConsoleLogger.Success($"第 {data.Paged} 页完成，共 {r.Posts.Count} 条");
                        _lastRequest = DateTime.UtcNow;
                        return r.Posts;
                    }
                }

                // 会话过期，刷新页面重新获取凭据
                ConsoleLogger.Warning("会话已过期，正在刷新凭据...");
                await _page!.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
                _loginNonce = await FetchNonceFromPageAsync();
            }
            catch (Exception e)
            {
                ConsoleLogger.Exception(e);
                if (coolDown > CoolDownThreshold)
                    throw;
                ConsoleLogger.Warning($"搜索请求失败: {e.Message}，{coolDown.TotalSeconds:0} 秒后重试");
                coolDown *= 2;
                await Task.Delay(coolDown);
            }
    }

    /// <summary>
    /// 通过浏览器页面上下文执行 fetch 请求，自动携带 cookies 和过挑战
    /// </summary>
    private async Task<string> PageFetchAsync(string relativeUrl)
    {
        return await _page!.EvaluateAsync<string>(
            """
            async (url) => {
                const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                return await response.text();
            }
            """, relativeUrl);
    }

    public Task WhenAllDownloadAsync() => Task.WhenAll(_imageDownloadTasks);

    public void DownloadThumbnailAddToList(Post post, string imagePath)
    {
        _imageDownloadTasks.Add(DownloadThumbnailAsync(post, imagePath));
    }

    public async Task DownloadThumbnailAsync(Post post, string imagePath)
    {
        var postThumbnailUrl = post.Thumbnail.Url;

        // 处理相对 URI
        if (!postThumbnailUrl.IsAbsoluteUri)
        {
            var originalString = postThumbnailUrl.OriginalString;
            postThumbnailUrl = originalString.StartsWith("//")
                ? new Uri("https:" + originalString)
                : new(new(Domain), postThumbnailUrl);
        }

        var fileName = post.ThumbnailFileName;
        var imgPath = Path.Combine(imagePath, post.ThumbnailFileName);
        if (File.Exists(imgPath))
            return;
        await _downloadSemaphore.WaitAsync();
        try
        {
            // 优先使用 HttpClient 直接下载（更快更稳定）
            if (await TryDownloadWithHttpClientAsync(postThumbnailUrl, imgPath))
            {
                ConsoleLogger.Success($"缩略图完成  #{post.Id}  {fileName}");
                return;
            }

            // 回退到浏览器内 fetch 下载，自动带上完整的浏览器环境
            if (await TryDownloadWithBrowserAsync(postThumbnailUrl, imgPath))
            {
                ConsoleLogger.Success($"缩略图完成  #{post.Id}  {fileName}  [浏览器回退]");
                return;
            }

            ConsoleLogger.Error($"Download thumbnail failed [{post.Id}]: {postThumbnailUrl}");
        }
        catch (Exception e)
        {
            ConsoleLogger.Exception(e);
            ConsoleLogger.Error($"Download thumbnail failed [{post.Id}]: {postThumbnailUrl} ({post.Url})");
            if (File.Exists(imgPath))
                File.Delete(imgPath);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task<bool> TryDownloadWithHttpClientAsync(Uri url, string imgPath)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri(Domain);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return false;

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.OpenAsyncWrite(imgPath, FileMode.Create);
            await stream.CopyToAsync(fileStream);
            return true;
        }
        catch
        {
            if (File.Exists(imgPath))
                File.Delete(imgPath);
            return false;
        }
    }

    private async Task<bool> TryDownloadWithBrowserAsync(Uri url, string imgPath)
    {
        try
        {
            var base64 = await _page!.EvaluateAsync<string?>(
                """
                async (url) => {
                    try {
                        const resp = await fetch(url, { referrer: 'https://www.mhh1.com/', credentials: 'include' });
                        if (!resp.ok) return null;
                        const buf = await resp.arrayBuffer();
                        const bytes = new Uint8Array(buf);
                        let binary = '';
                        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                        return btoa(binary);
                    } catch { return null; }
                }
                """, url.AbsoluteUri);

            if (string.IsNullOrEmpty(base64))
                return false;

            var body = Convert.FromBase64String(base64);
            await File.WriteAllBytesAsync(imgPath, body);
            return true;
        }
        catch
        {
            if (File.Exists(imgPath))
                File.Delete(imgPath);
            return false;
        }
    }

    private async Task SyncCookiesToHttpClientAsync()
    {
        var cookies = await _browserContext!.CookiesAsync([Domain]);
        foreach (var c in cookies)
        {
            _cookieContainer.Add(new Uri(Domain), new Cookie(c.Name, c.Value, c.Path, c.Domain));
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _downloadSemaphore.Dispose();
        if (_browserContext is not null)
            await _browserContext.CloseAsync();
        _playwright?.Dispose();
    }
}
