using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HMoeViewer.Extraction;

public sealed record Post(int Id, string Title, string Url);

public sealed record LinkOrigin(string Source, string Kind, int? DownloadItemIndex, string OriginalUrl);

public sealed record Link(string Url, string? ExtractionCode, string? ArchivePassword, List<LinkOrigin> Origins);

public sealed record ImageScan(string Url, string Source, int? DownloadItemIndex, string[] Payloads, string? Error);

public sealed record DownloadItem(int Index, string Label, string? ExtractionCode, string? ArchivePassword, List<string> Targets);

public sealed record PostResult(int Id, string Title, string Url, string Status, List<Link> Links,
    List<ImageScan> Images, List<DownloadItem> Downloads, List<string> Errors, DateTimeOffset CheckedAt, int FormatVersion = 0);

[JsonSerializable(typeof(PostResult))]
public partial class OutputJsonContext : JsonSerializerContext;
