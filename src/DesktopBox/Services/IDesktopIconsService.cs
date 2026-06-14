namespace DesktopBox.Services;

public interface IDesktopIconsService
{
    /// <summary>当前桌面图标是否可见。</summary>
    bool AreIconsVisible { get; }

    /// <summary>设置桌面图标显隐(全局开关,可逆;不移动任何文件)。</summary>
    void SetVisible(bool visible);
}
