using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HMoeViewer.Extraction;

public static partial class LinkRules
{
    public static bool IsValidTarget(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "magnet" or "ed2k" or "thunder";

    public static string? Resolve(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('#'))
            return null;
        if (!Uri.TryCreate(new Uri(source), value.Trim(), out var uri))
            return null;
        return uri.Scheme is "http" or "https" or "magnet" or "ed2k" or "thunder" ? uri.AbsoluteUri : null;
    }

    public static bool IsDownload(string url)
    {
        if (!IsValidTarget(url))
            return false;
        var uri = new Uri(url);
        if (uri.Scheme is "magnet" or "ed2k" or "thunder")
            return true;
        var host = uri.IdnHost;
        return new[] { "pan.baidu.com", "mypikpak.com", "pan.quark.cn", "pan.xunlei.com", "115.com", "115cdn.com",
            "123pan.com", "123pan.cn", "123684.com", "123865.com", "123912.com", "aliyundrive.com", "alipan.com",
            "mega.nz", "mega.io", "mediafire.com", "drive.google.com", "1drv.ms", "onedrive.live.com", "cloud.189.cn",
            "pan.uc.cn", "pixeldrain.com", "pixeldrain.net", "gofile.io", "workupload.com", "catbox.moe", "lanzou.com", "lanzoux.com",
            "lanzoui.com", "lanzous.com", "lanzouf.com", "lanzouj.com", "lanzoup.com", "lanzout.com", "lanzouv.com" }
            .Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                           || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase))
            || ArchiveExtension().IsMatch(uri.AbsolutePath);
    }

    public static bool IsImage(string url) => IsValidTarget(url) && ImageExtension().IsMatch(new Uri(url).AbsolutePath);

    public static bool IsSite(string url) => new Uri(url).Host.Equals("www.mhh1.com", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> FindUrls(string text) => UrlPattern().Matches(text)
        .Select(match => match.Value.TrimEnd('.', ',', ';', ')', ']', '}', '。', '，', '；', '）', '】'));

    public static string Compact(string text) => Whitespace().Replace(text, " ").Trim();

    public static bool DownloadLabel(string text) => DownloadWords().IsMatch(text);

    [GeneratedRegex(@"(?:https?://|magnet:\?|ed2k://|thunder://)[^\s<>""'\u3000-\u303f\u4e00-\u9fff]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\.(png|jpe?g|webp|gif|bmp|tiff?|avif)(?:$)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageExtension();

    [GeneratedRegex(@"\.(zip|7z|rar|torrent|tar|gz|001)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchiveExtension();

    [GeneratedRegex(@"下载|網盤|网盘|提取|download|二维码|二維碼", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
