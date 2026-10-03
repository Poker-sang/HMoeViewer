using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace HMoeViewer.Controls.Browsing;

public partial class CompactDownloadRow : DownloadPostControl
{
    public CompactDownloadRow()
    {
        InitializeComponent();
        ToolTip.AddToolTipOpeningHandler(PreviewHost, (_, e) =>
        {
            if (ReferenceEquals(e.Source, PreviewHost) && IsPreviewSuppressed)
                e.Cancel = true;
        });
        AddHandler(PointerMovedEvent, PreviewPointerMoved, RoutingStrategies.Tunnel, true);
        PreviewHost.PointerExited += (_, _) => CancelPreview();
        DetachedFromVisualTree += (_, _) => CancelPreview();
    }

    private Point? _pointerPosition;
    private IDisposable? _previewTimer;

    private bool IsPreviewSuppressed => _pointerPosition is { } pointer && this.GetVisualDescendants().OfType<Control>().Any(control =>
        control.IsEffectivelyVisible && (control is Button || control.Classes.Contains("download-value"))
        && control.TranslatePoint(default, this) is { } origin && new Rect(origin, control.Bounds.Size).Contains(pointer));

    private void PreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        _pointerPosition = e.GetPosition(this);
        CoverTooltip.TrackPointer(PreviewHost, e);
        // 文字控件不一定参与命中测试，用实际区域判断，避免密码上仍弹出封面。
        if (IsPreviewSuppressed)
        {
            CancelPreview();
            return;
        }
        // 同一行内从链接移回空白处不会重新触发 PointerEntered，需要重新安排预览。
        if (_previewTimer is null && !ToolTip.GetIsOpen(PreviewHost))
            _previewTimer = DispatcherTimer.RunOnce(() =>
            {
                _previewTimer = null;
                if (PreviewHost.IsPointerOver && !IsPreviewSuppressed)
                    ToolTip.SetIsOpen(PreviewHost, true);
            }, TimeSpan.FromMilliseconds(ToolTip.GetShowDelay(PreviewHost)));
    }

    private void CancelPreview()
    {
        _previewTimer?.Dispose();
        _previewTimer = null;
        ToolTip.SetIsOpen(PreviewHost, false);
    }

}
