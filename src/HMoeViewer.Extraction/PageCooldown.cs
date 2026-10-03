using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HMoeViewer.Extraction;

/// <summary>One shared navigation budget for foreground extraction and background caching.</summary>
public sealed class PageCooldown(TimeSpan interval)
{
    public static PageCooldown Shared { get; } = new(TimeSpan.FromSeconds(5));

    private readonly SemaphoreSlim _gate = new(1);
    private long? _lastRequest;

    public async Task WaitAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_lastRequest is { } last)
            {
                var remaining = interval - Stopwatch.GetElapsedTime(last);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, token).ConfigureAwait(false);
            }
            _lastRequest = Stopwatch.GetTimestamp();
        }
        finally
        {
            _ = _gate.Release();
        }
    }
}
