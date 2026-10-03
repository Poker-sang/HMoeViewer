using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using HMoeViewer.Core;
using HMoeData.Models;
using SmoothScroll.Avalonia.Controls;

namespace HMoeViewer.Controls.Browsing;

public partial class PostCard : BrowserControl
{
    private async void TemporaryAccept(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: PostItemViewModel item } && ViewModel.TemporaryAcceptCommand.CanExecute(item))
            await ViewModel.TemporaryAcceptCommand.ExecuteAsync(item);
    }

    private async void SendToClient(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button button && DataContext is PostItemViewModel item)
            await SendDownloadsToClientAsync(button, item.DownloadLinks);
    }
    public PostCard() => InitializeComponent();

    private static readonly IBrush _UnselectedBrush = new ImmutableSolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush _SelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush _DeselectedBrush = new ImmutableSolidColorBrush(Color.Parse("#B45309"));
    private static readonly IBrush _DeletedBrush = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));

    public static FuncValueConverter<PostSelectionState, IBrush> StateBackground { get; } = new(state => state switch
    {
        PostSelectionState.Unselected => _UnselectedBrush,
        PostSelectionState.Selected => _SelectedBrush,
        PostSelectionState.Deselected => _DeselectedBrush,
        PostSelectionState.Deleted => _DeletedBrush,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    });

    public static FuncValueConverter<PostCacheState, IBrush> CacheFill { get; } = new(state => state switch
    {
        PostCacheState.None => Brushes.Gray,
        PostCacheState.Body => _SelectedBrush,
        PostCacheState.Downloads => Brushes.ForestGreen,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    });

    private void CardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Control card)
            CoverTooltip.TrackPointer(card, e);
    }

    private static bool IsActionControl(object? source)
    {
        var control = source as Avalonia.Visual;
        while (control is not null)
        {
            if (control is Button or CheckBox or SelectableTextBlock)
                return true;
            control = control.GetVisualParent();
        }

        return false;
    }

    private async void PostTapped(object? sender, TappedEventArgs e)
    {
        if (IsActionControl(e.Source) || sender is not Border { DataContext: PostItemViewModel item })
            return;
        if (ViewModel.BrowseMode is BrowseMode.Detail or BrowseMode.Download)
            await OpenReviewAsync(item);
        else if (!ViewModel.IsBrief)
            await OpenUrlAsync(item.Post.Url, item);
        else
            item.IsSelected = !item.IsSelected;
    }

    private async void PostDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsActionControl(e.Source) && ViewModel.IsBrief && sender is Border { DataContext: PostItemViewModel item })
            await OpenReviewAsync(item);
    }

    private void RemovePost(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PostItemViewModel item })
            ViewModel.RemoveCommand.Execute(item);
        e.Handled = true;
    }

    private void TooltipPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (this.GetVisualAncestors().OfType<ScrollViewer>().First().GetVisualDescendants().OfType<ScrollViewerPresenter>().FirstOrDefault() is not { } presenter)
            return;
        // Tooltips have a separate popup root; route wheel input through the list's smooth presenter.
        var forwarded = new PointerWheelEventArgs(presenter, e.Pointer, TopLevel.GetTopLevel(this)!, e.GetPosition(TopLevel.GetTopLevel(this)), e.Timestamp, e.GetCurrentPoint(null).Properties, e.KeyModifiers, e.Delta);
        presenter.RaiseEvent(forwarded);
        e.Handled = true;
    }

}
