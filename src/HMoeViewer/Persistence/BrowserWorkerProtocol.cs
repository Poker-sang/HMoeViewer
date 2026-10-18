using System;
using System.Text.Json.Serialization;
using HMoeViewer.Core;
using HMoeViewer.Downloads;
using HMoeViewer.Extraction;

namespace HMoeViewer.Persistence;

public sealed record BrowserWorkerRequest(string Id, string Command, SelectionBatch? Batch = null,
    Post? Post = null, ClientDownloadRequest? Download = null, string? ProfileDirectory = null);

public sealed record BrowserWorkerResponse(string Id, string Event, string? Error = null,
    string? Html = null, string? Directory = null, string[]? Images = null, string? Image = null,
    PostResult? Result = null, ClientDownloadResult? Download = null, SelectionBatch? Batch = null, int PostId = 0);

[JsonSerializable(typeof(BrowserWorkerRequest))]
[JsonSerializable(typeof(BrowserWorkerResponse))]
public partial class BrowserWorkerJsonContext : JsonSerializerContext;
