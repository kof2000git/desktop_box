using System.Windows;

namespace DesktopBox.Controls;

public static class BoxResize
{
    public const double MinWidth = 140;
    public const double MinHeight = 120;

    public static Rect Apply(Rect origin, string direction, double dx, double dy)
    {
        if (!IsValidDirection(direction))
            return origin;

        var x = origin.X;
        var y = origin.Y;
        var width = origin.Width;
        var height = origin.Height;

        if (direction.Contains('E'))
            width = Math.Max(MinWidth, origin.Width + dx);

        if (direction.Contains('S'))
            height = Math.Max(MinHeight, origin.Height + dy);

        if (direction.Contains('W'))
        {
            width = Math.Max(MinWidth, origin.Width - dx);
            x = origin.Right - width;
        }

        if (direction.Contains('N'))
        {
            height = Math.Max(MinHeight, origin.Height - dy);
            y = origin.Bottom - height;
        }

        return new Rect(x, y, width, height);
    }

    private static bool IsValidDirection(string direction) =>
        direction is "N" or "S" or "W" or "E" or "NW" or "NE" or "SW" or "SE";
}
