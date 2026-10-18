using System;
using System.Threading;
using System.Threading.Tasks;
using HMoeViewer.Downloads;

namespace HMoeViewer.Persistence;

public sealed class WorkerClientDownloadService(BrowserWorkerClient worker, string profileDirectory) : IClientDownloadService
{
    public async Task<ClientDownloadResult> SendAsync(ClientDownloadRequest request, CancellationToken token = default) =>
        (await worker.InvokeAsync(new(Guid.NewGuid().ToString("N"), "download", Download: request, ProfileDirectory: profileDirectory), token).ConfigureAwait(false)).Download!;

    // The application owns the shared worker; disposing one facade must not stop the other.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
