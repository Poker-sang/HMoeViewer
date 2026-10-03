using Avalonia.Controls;
using Avalonia.Interactivity;
using HMoeViewer.Core;

namespace HMoeViewer.Controls.Browsing;

public class DownloadPostControl : BrowserControl
{
    protected async void TemporaryAccept(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: PostItemViewModel item } && ViewModel.TemporaryAcceptCommand.CanExecute(item))
            await ViewModel.TemporaryAcceptCommand.ExecuteAsync(item);
    }

    protected void RemovePost(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PostItemViewModel item })
            ViewModel.RemoveCommand.Execute(item);
        e.Handled = true;
    }

    protected async void OpenArticle(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PostItemViewModel item })
            await OpenReviewAsync(item);
    }
}
