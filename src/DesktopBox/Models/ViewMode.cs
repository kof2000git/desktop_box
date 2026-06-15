namespace DesktopBox.Models;

/// <summary>盒子内部条目的视图布局(对应资源管理器的"查看"模式)。</summary>
public enum ViewMode
{
    Large = 0,        // 大图标
    Medium = 1,       // 中等图标
    Small = 2,        // 小图标
    List = 3,         // 列表
    Detail = 4,       // 详细信息(名称/修改时间/大小)
    Tile = 5,         // 平铺
}
