using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMoeViewer.Downloads;
using HMoeViewer.Extraction;

namespace HMoeViewer.Core;

public partial class MainViewModel
{
    public IClientDownloadService? ClientDownloads { get; init; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendAllToClientCommand))]
    public partial bool IsSendingToClient { get; set; }

    private bool CanSendAllToClient() => !IsSendingToClient && IsDownload && ClientDownloads is not null && Articles is not null && CurrentBatch is not null;

    [RelayCommand(CanExecute = nameof(CanSendAllToClient))]
    public async Task SendAllToClientAsync()
    {
        if (!CanSendAllToClient() || CurrentBatch is not { } batch || Articles is not { } articles || ClientDownloads is not { } client)
            return;
        var items = _allItems.Where(item => item.IsPendingDownloadReview).ToArray();
        var sent = new Dictionary<(string Url, string? Code), ClientDownloadStatus>();
        IsSendingToClient = true;
        Status = "批量发送已排队，等待正文和下载链接获取完成。";
        await _articleBatchGate.WaitAsync();
        try
        {
            await articles.WaitForQueuedCacheAsync();
            for (var index = 0; index < items.Length; ++index)
            {
                var item = items[index];
                Status = $"发送到客户端 {index + 1}/{items.Length}：{item.Title}";
                try
                {
                    if (item.DownloadLinks.Count is 0)
                    {
                        var result = await articles.AcceptAsync(batch, new(item.Post.Id, item.Title, item.Post.Url.AbsoluteUri));
                        if (CurrentBatch == batch && _allItems.Contains(item))
                            CompleteAcceptance(item, result);
                        await SendLinksAsync(result.Links);
                    }
                    else
                        await SendLinksAsync(item.DownloadLinks);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Status = $"#{item.Post.Id}：{ex.Message}";
                }

                async Task SendLinksAsync(IReadOnlyList<Link> links)
                {
                    var supported = links.Where(link => BaiduClientDownloadService.Supports(link.Url)).ToArray();
                    foreach (var link in supported)
                    {
                        var key = (link.Url, link.ExtractionCode);
                        if (sent.TryGetValue(key, out var previous))
                        {
                            if (previous is ClientDownloadStatus.Submitted && CurrentBatch == batch)
                                _ = await MarkDownloadedAsync(item);
                            continue;
                        }
                        try
                        {
                            var result = await client.SendAsync(new(link.Url, link.ExtractionCode));
                            sent[key] = result.Status;
                            if (result.Status is ClientDownloadStatus.Submitted && CurrentBatch == batch)
                                _ = await MarkDownloadedAsync(item);
                            Status = $"#{item.Post.Id}：{result.Message}";
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Status = $"#{item.Post.Id}：{ex.Message}";
                        }
                    }
                }
            }
            Status = "批量发送已结束。";
        }
        catch (OperationCanceledException)
        {
            Status = "批量发送已取消。";
        }
        catch (Exception ex)
        {
            Status = $"批量发送失败：{ex.Message}";
        }
        finally
        {
            _ = _articleBatchGate.Release();
            IsSendingToClient = false;
        }
    }

    public async Task SendToClientAsync(Link link, PostItemViewModel? item = null)
    {
        if (IsSendingToClient)
            return;
        if (ClientDownloads is null)
        {
            Status = "客户端下载服务未启用。";
            return;
        }
        IsSendingToClient = true;
        var batch = CurrentBatch;
        Status = "正在后台发送到百度网盘客户端。";
        try
        {
            var result = await ClientDownloads.SendAsync(new(link.Url, link.ExtractionCode));
            if (result.Status is ClientDownloadStatus.Submitted && CurrentBatch == batch)
            {
                var owners = item is not null ? new[] { item } : _allItems.Where(candidate => candidate.DownloadLinks.Contains(link)).ToArray();
                foreach (var owner in owners)
                    _ = await MarkDownloadedAsync(owner);
            }
            Status = result.Message;
        }
        catch (Exception ex)
        {
            Status = $"发送到客户端失败：{ex.Message}";
        }
        finally
        {
            IsSendingToClient = false;
        }
    }
}
