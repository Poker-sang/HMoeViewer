using System;
using System.Linq;

namespace HMoeViewer.Extraction;

public static class LinkCollector
{
    public const int FormatVersion = 2;

    public static void Add(PostResult result, string url, string source, string kind, DownloadItem? item)
    {
        if (!LinkRules.IsValidTarget(url))
            return;
        var code = GetEmbeddedCode(url) ?? item?.ExtractionCode;
        var password = item?.ArchivePassword;
        var origin = new LinkOrigin(source, kind, item?.Index, url);
        var key = GetComparisonKey(url);
        var existing = result.Links.FirstOrDefault(link => GetComparisonKey(link.Url) == key
            && link.ExtractionCode == code && link.ArchivePassword == password);
        if (existing is null)
            result.Links.Add(new Link(WithExtractionCode(url, code), code, password, [origin]));
        else if (!existing.Origins.Contains(origin))
            existing.Origins.Add(origin);
    }

    public static void Consolidate(PostResult result)
    {
        // Repair old cached URLs from their original sources locally; browser error pages are never links.
        _ = result.Links.RemoveAll(link => !LinkRules.IsValidTarget(link.Url));
        foreach (var item in result.Downloads)
            _ = item.Targets.RemoveAll(url => !LinkRules.IsValidTarget(url));
        for (var i = 0; i < result.Links.Count; ++i)
        {
            var link = result.Links[i];
            var original = RestoreOriginalUrl(link);
            var code = GetEmbeddedCode(original) ?? link.ExtractionCode;
            result.Links[i] = link with { Url = WithExtractionCode(original, code), ExtractionCode = code };
        }

        // Compare share identities internally while keeping complete URLs in displayed and copied results.
        foreach (var partial in result.Links.OrderByDescending(link => (link.ExtractionCode is null ? 0 : 1)
                     + (link.ArchivePassword is null ? 0 : 1)).ToArray())
        {
            var key = GetComparisonKey(partial.Url);
            var matches = result.Links.Where(link => !ReferenceEquals(link, partial) && GetComparisonKey(link.Url) == key
                && (partial.ExtractionCode is null || partial.ExtractionCode == link.ExtractionCode)
                && (partial.ArchivePassword is null || partial.ArchivePassword == link.ArchivePassword)
                && ((partial.ExtractionCode is null && link.ExtractionCode is not null)
                    || (partial.ArchivePassword is null && link.ArchivePassword is not null))).ToArray();
            if (matches.Length is not 1)
                continue;
            foreach (var origin in partial.Origins)
                if (!matches[0].Origins.Contains(origin))
                    matches[0].Origins.Add(origin);
            _ = result.Links.Remove(partial);
        }
        foreach (var group in result.Links.GroupBy(link => (GetComparisonKey(link.Url), link.ExtractionCode, link.ArchivePassword)).ToArray())
        {
            var first = group.First();
            foreach (var duplicate in group.Skip(1))
            {
                foreach (var origin in duplicate.Origins)
                    if (!first.Origins.Contains(origin))
                        first.Origins.Add(origin);
                _ = result.Links.Remove(duplicate);
            }
        }
        for (var i = 0; i < result.Links.Count; ++i)
        {
            var link = result.Links[i];
            result.Links[i] = link with { Url = WithExtractionCode(RestoreOriginalUrl(link), link.ExtractionCode) };
        }
    }

    public static string WithExtractionCode(string url, string? code)
    {
        url = NormalizeShareUrl(url);
        if (!IsBaidu(url) || code is not { Length: 4 } || !code.All(char.IsAsciiLetterOrDigit))
            return url;
        // Preserve every original query value and fragment. An existing nonempty pwd always wins in the URL.
        var fragmentIndex = url.IndexOf('#');
        var beforeFragment = fragmentIndex < 0 ? url : url[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
        var queryIndex = beforeFragment.IndexOf('?');
        if (queryIndex >= 0)
        {
            var pairs = beforeFragment[(queryIndex + 1)..].Split('&');
            for (var i = 0; i < pairs.Length; ++i)
            {
                var pair = pairs[i].Split('=', 2);
                if (!Uri.UnescapeDataString(pair[0]).Equals("pwd", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (pair is [_, { Length: > 0 }])
                    return url;
                pairs[i] = pair[0] + "=" + Uri.EscapeDataString(code);
                return beforeFragment[..(queryIndex + 1)] + string.Join('&', pairs) + fragment;
            }
        }
        var separator = queryIndex < 0 ? "?" : beforeFragment.EndsWith('?') || beforeFragment.EndsWith('&') ? "" : "&";
        return beforeFragment + separator + "pwd=" + Uri.EscapeDataString(code) + fragment;
    }

    private static string NormalizeShareUrl(string url)
    {
        if (!IsBaidu(url) || new Uri(url).AbsolutePath is not "/share/init")
            return url;
        var fragmentIndex = url.IndexOf('#');
        var beforeFragment = fragmentIndex < 0 ? url : url[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
        var queryIndex = beforeFragment.IndexOf('?');
        if (queryIndex < 0)
            return url;
        var pairs = beforeFragment[(queryIndex + 1)..].Split('&');
        var shares = pairs.Select(pair => pair.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]).Equals("surl", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (shares is not [[_, { Length: > 0 } share]])
            return url;
        var id = Uri.UnescapeDataString(share);
        if (!id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            return url;
        // Move only the share ID into the path; retain the spelling and encoding of every other value.
        var remaining = pairs.Where(pair => !Uri.UnescapeDataString(pair.Split('=', 2)[0])
            .Equals("surl", StringComparison.OrdinalIgnoreCase)).ToArray();
        return new Uri(url).GetLeftPart(UriPartial.Authority) + "/s/1" + id
            + (remaining.Length is 0 ? "" : "?" + string.Join('&', remaining)) + fragment;
    }

    private static string RestoreOriginalUrl(Link link)
    {
        var key = GetComparisonKey(link.Url);
        return link.Origins.Select(origin => origin.OriginalUrl).Prepend(link.Url)
            .Where(url => LinkRules.IsValidTarget(url) && GetComparisonKey(url) == key
                && (GetEmbeddedCode(url) is not { } code || link.ExtractionCode is not { Length: 4 } || code == link.ExtractionCode))
            .OrderByDescending(url => new Uri(url).Query.Length)
            .FirstOrDefault() ?? link.Url;
    }

    private static bool IsBaidu(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Equals("pan.baidu.com", StringComparison.OrdinalIgnoreCase);

    private static string? GetEmbeddedCode(string url)
    {
        if (!IsBaidu(url))
            return null;
        var query = new Uri(url).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var value in query)
        {
            var pair = value.Split('=', 2);
            if (pair is [var name, { Length: > 0 } code] && Uri.UnescapeDataString(name).Equals("pwd", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(code);
        }
        return null;
    }

    private static string GetComparisonKey(string url)
    {
        if (!IsBaidu(url))
            return url;
        url = NormalizeShareUrl(url);
        var uri = new Uri(url);
        var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (!uri.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal))
            return url;
        var remaining = pairs.Where(pair =>
        {
            var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
            return !name.Equals("pwd", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        return "https://pan.baidu.com" + uri.AbsolutePath + (remaining.Length is 0 ? "" : "?" + string.Join('&', remaining)) + uri.Fragment;
    }
}
