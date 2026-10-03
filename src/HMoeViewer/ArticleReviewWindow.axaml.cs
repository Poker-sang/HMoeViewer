using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HMoeViewer.Core;

namespace HMoeViewer;

public partial class ArticleReviewWindow : Window
{
    public ArticleReviewWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is ArticleReviewViewModel model)
                await model.LoadAsync();
        };
        Closed += (_, _) => (DataContext as ArticleReviewViewModel)?.Dispose();
    }

    private async void OpenOriginal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArticleReviewViewModel { Current: { } item } model)
            return;
        try
        {
            if (item.Post.Url.Scheme is "http" or "https")
            {
                if (await Launcher.LaunchUriAsync(item.Post.Url))
                    _ = await model.MarkViewedAsync(item);
                else
                    model.Status = "无法打开默认浏览器。";
            }
        }
        catch (Exception ex)
        {
            model.Status = $"打开原网页失败：{ex.Message}";
        }
    }
}
