using System.Windows;

namespace DesktopBox.Controls;

public static class EdgeMagnet
{
    public const double SnapDistance = 12;
    public const double Margin = 4;

    public static double ApplyHorizontal(BoxEdgeSnapState state, double x, double width, IEnumerable<Rect> screens)
    {
        state.Snapped = false;
        foreach (var screen in screens)
        {
            var left = screen.Left + Margin;
            var right = screen.Right - width - Margin;

            if (ShouldSnap(x, state, left, isMinEdge: true))
            {
                state.Snapped = true;
                state.Edge = left;
                state.IsMinEdge = true;
                return left;
            }

            if (ShouldSnap(x, state, right, isMinEdge: false))
            {
                state.Snapped = true;
                state.Edge = right;
                state.IsMinEdge = false;
                return right;
            }
        }

        state.Edge = null;
        return x;
    }

    public static double ApplyVertical(BoxEdgeSnapState state, double y, double height, IEnumerable<Rect> screens)
    {
        state.Snapped = false;
        foreach (var screen in screens)
        {
            var top = screen.Top + Margin;
            var bottom = screen.Bottom - height - Margin;

            if (ShouldSnap(y, state, top, isMinEdge: true))
            {
                state.Snapped = true;
                state.Edge = top;
                state.IsMinEdge = true;
                return top;
            }

            if (ShouldSnap(y, state, bottom, isMinEdge: false))
            {
                state.Snapped = true;
                state.Edge = bottom;
                state.IsMinEdge = false;
                return bottom;
            }
        }

        state.Edge = null;
        return y;
    }

    private static bool ShouldSnap(double value, BoxEdgeSnapState state, double edge, bool isMinEdge)
    {
        var distance = value - edge;
        if (!state.Snapped)
            return Math.Abs(distance) <= SnapDistance;

        if (state.Edge is null || Math.Abs(state.Edge.Value - edge) > 0.01 || state.IsMinEdge != isMinEdge)
            return Math.Abs(distance) <= SnapDistance;

        return isMinEdge ? distance <= SnapDistance : distance >= -SnapDistance;
    }
}

public sealed class BoxEdgeSnapState
{
    public bool Snapped { get; set; }
    public double? Edge { get; set; }
    public bool IsMinEdge { get; set; }
}
