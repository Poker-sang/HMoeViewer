using System;
using System.Threading;
using System.Threading.Tasks;

namespace HMoeViewer.Downloads;

public enum ClientDownloadStatus
{
    Submitted,
    NeedsInteraction,
    NeedsVerification,
    Failed,
    Unsupported
}

public sealed record ClientDownloadRequest(string Url, string? ExtractionCode = null);

public sealed record ClientDownloadResult(ClientDownloadStatus Status, string Message);

public interface IClientDownloadService : IAsyncDisposable
{
    Task<ClientDownloadResult> SendAsync(ClientDownloadRequest request, CancellationToken token = default);
}
