using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HMoeViewer.Extraction;

namespace HMoeViewer.Controls.Browsing;

public partial class DownloadLinkRow : BrowserControl
{
    public DownloadLinkRow() => InitializeComponent();

    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<DownloadLinkRow, bool>(nameof(IsCompact));

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private async void CopyDownload(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Link link })
            await CopyDownloadTextAsync(link.Url);
    }

    private async void OpenDownload(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Link link } && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
            await OpenUrlAsync(uri);
    }

    private async void SendToClient(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Link link)
            await ViewModel.SendToClientAsync(link, PostItem);
    }

}
