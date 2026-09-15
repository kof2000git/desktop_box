using System;
using DesktopBox.Native;
using Microsoft.Win32;

namespace DesktopBox.Services;

/// <summary>
/// 控制 Windows 桌面图标显隐。优先读取 Explorer 桌面 ListView 的实际可见性,
/// 注册表 HideIcons 仅作为兜底状态来源/持久化状态,
/// 用 WM_COMMAND 0x7073 发给 SHELLDLL_DefView 来即时切换(实测在 Win11 26200 有效)。
/// 不移动任何文件,纯视觉开关。
/// </summary>
public class DesktopIconsService : IDesktopIconsService
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "HideIcons";
    private const int TOGGLE_CMD = 0x7073; // 实测有效的桌面图标显隐切换命令

    public bool AreIconsVisible
    {
        get
        {
            try
            {
                var listView = User32.FindDesktopListView();
                if (listView != IntPtr.Zero)
                    return User32.IsWindowVisible(listView);

                using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
                return key?.GetValue(ValueName) is not int v || v == 0;
            }
            catch { return true; }
        }
    }

    public void SetVisible(bool visible)
    {
        if (AreIconsVisible == visible) return; // 状态已一致,无需切换
        try
        {
            var def = User32.FindShellDefView();
            if (def != IntPtr.Zero && User32.IsWindow(def))
            {
                // 0x7073 是"切换"命令。必须用带超时的 SendMessageTimeout 跨进程发送：
                // explorer 卡顿时 SendMessage 会无限卡住 UI 线程（历史卡死主因）。
                var ok = User32.SendMessageTimeout(def, User32.WM_COMMAND, (IntPtr)TOGGLE_CMD, IntPtr.Zero,
                    User32.SMTO_ABORTIFHUNG, 2000, out _) != IntPtr.Zero;
                if (!ok)
                    LogService.Warn("DesktopIcons.Toggle", "SendMessageTimeout 超时/失败，已只写注册表兜底");
                SetRegistry(visible ? 0 : 1);
                LogService.Info("DesktopIcons.Toggle", $"targetVisible={visible} sendOk={ok}");
                return;
            }
            // 兜底:只写注册表(下次 explorer 刷新后生效)
            SetRegistry(visible ? 0 : 1);
            LogService.Info("DesktopIcons.Toggle", $"targetVisible={visible} viaRegistryOnly (defView=0)");
        }
        catch (Exception ex) { LogService.Error(ex, "DesktopIcons.SetVisible"); }
    }

    private static void SetRegistry(int hideIcons)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AdvancedKey);
        key?.SetValue(ValueName, hideIcons, RegistryValueKind.DWord);
    }
}
