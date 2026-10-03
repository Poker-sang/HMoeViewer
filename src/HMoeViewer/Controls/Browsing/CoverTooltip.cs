using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;

namespace HMoeViewer.Controls.Browsing;

public sealed class CoverTooltip
{
    private static readonly AttachedProperty<Point> _PointerPositionProperty =
        AvaloniaProperty.RegisterAttached<CoverTooltip, Control, Point>("PointerPosition");

    public static CustomPopupPlacementCallback Place { get; } = placement =>
    {
        var point = placement.Target.GetValue(_PointerPositionProperty);
        // 自定义回调的锚点属于窗口坐标系，不能使用卡片内部坐标。
        // 上下各留 50px，翻转到鼠标下方时也能保留相同的距离。
        placement.AnchorRectangle = new Rect(point - new Vector(0, 50), new Size(1, 100));
        placement.Anchor = PopupAnchor.Top;
        placement.Gravity = PopupGravity.Top;
        placement.Offset = default;
        placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY
            | PopupPositionerConstraintAdjustment.SlideX
            | PopupPositionerConstraintAdjustment.SlideY;
    };

    public static void TrackPointer(Control host, PointerEventArgs e) =>
        host.SetValue(_PointerPositionProperty, e.GetPosition(TopLevel.GetTopLevel(host)));
}
