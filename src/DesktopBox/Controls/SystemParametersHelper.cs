using System.Collections.Generic;
using System.Windows;

namespace DesktopBox.Controls;

/// <summary>获取所有显示器的工作区边界(含多屏拼接),用于盒子边缘磁吸。</summary>
public static class SystemParametersHelper
{
    public static IReadOnlyList<Rect> AllScreens => GetScreens();

    public static double LayoutWidth
    {
        get
        {
            var screens = AllScreens;
            if (screens.Count == 0) return SystemParameters.PrimaryScreenWidth;
            return screens.Max(s => s.Right) - screens.Min(s => s.Left);
        }
    }

    private static List<Rect> GetScreens()
    {
        var list = new List<Rect>();
        try
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                list.Add(new Rect(
                    s.WorkingArea.X, s.WorkingArea.Y,
                    s.WorkingArea.Width, s.WorkingArea.Height));
        }
        catch
        {
            list.Add(SystemParameters.WorkArea);
        }
        return list;
    }

    /// <summary>约束盒子左上角:保证 keepW×keepH 的区域留在某个屏幕工作区内。
    /// 防止拖到屏外、或加载到已失效的旧坐标(换分辨率/拔屏幕)后窗口找不到。返回应使用的 (X,Y)。</summary>
    public static (double X, double Y) ClampIntoScreens(double x, double y, double keepW = 120, double keepH = 40)
    {
        // 当前保留区已在某屏内 → 合法,原样返回
        foreach (var s in AllScreens)
            if (x >= s.Left && y >= s.Top && x + keepW <= s.Right && y + keepH <= s.Bottom)
                return (x, y);
        // 不合法:移到第一个能容纳保留区的屏幕左上角
        foreach (var s in AllScreens)
            if (s.Width >= keepW + 16 && s.Height >= keepH + 16)
                return (s.Left + 8, s.Top + 8);
        // 兜底:主工作区
        var wa = SystemParameters.WorkArea;
        return (wa.X + 8, wa.Y + 8);
    }
}
