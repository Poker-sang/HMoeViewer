using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HMoeViewer.Core;
using HMoeViewer.Downloads;
using HMoeViewer.Extraction;

namespace HMoeViewer.Controls.Browsing;

public class BrowserControl : UserControl
{
    protected MainViewModel ViewModel => DataContext as MainViewModel
        ?? this.GetVisualAncestors().OfType<Control>().Select(control => control.DataContext).OfType<MainViewModel>().First();

    protected PostItemViewModel? PostItem => DataContext as PostItemViewModel
        ?? this.GetVisualAncestors().OfType<Control>().Select(control => control.DataContext).OfType<PostItemViewModel>().FirstOrDefault();

    protected async Task OpenReviewAsync(PostItemViewModel item)
    {
        try
        {
            var window = new ArticleReviewWindow { DataContext = new ArticleReviewViewModel(ViewModel, item) };
            await window.ShowDialog((Window) TopLevel.GetTopLevel(this)!);
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"无法打开正文：{ex.Message}";
        }
    }

    protected async Task SendDownloadsToClientAsync(Button button, IReadOnlyList<Link> downloads)
    {
        var owner = PostItem;
        var links = downloads.Where(link => BaiduClientDownloadService.Supports(link.Url)).ToArray();
        if (links.Length is 0)
        {
            ViewModel.Status = "此作品没有百度网盘分享链接。";
            return;
        }
        if (links.Length is 1)
        {
            await ViewModel.SendToClientAsync(links[0], owner);
            return;
        }
        var menu = new MenuFlyout();
        foreach (var link in links)
        {
            var entry = new MenuItem { Header = link.Url };
            entry.Click += async (_, _) => await ViewModel.SendToClientAsync(link, owner);
            menu.Items.Add(entry);
        }
        button.Flyout = menu;
        menu.ShowAt(button);
    }

    protected async Task CopyDownloadTextAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                ViewModel.Status = "当前无法访问剪贴板。";
                return;
            }
            var item = new DataTransferItem();
            item.Set(DataFormat.Text, text);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
            ViewModel.Status = "下载链接已复制。";
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"复制失败：{ex.Message}";
        }
    }

    protected async Task OpenUrlAsync(Uri url, PostItemViewModel? article = null)
    {
        try
        {
            if (!url.IsAbsoluteUri || url.Scheme is not ("https" or "http"))
                throw new InvalidOperationException("条目链接无效。");
            if (!await TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(url))
                ViewModel.Status = "无法启动默认浏览器。";
            else if (article is not null)
                _ = await ViewModel.MarkViewedAsync(article);
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"打开失败：{ex.Message}";
        }
    }

}
