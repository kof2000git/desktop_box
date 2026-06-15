namespace DesktopBox.Models;

/// <summary>磁贴在盒子中的渲染尺寸/布局(由盒子 ViewMode 决定)。</summary>
public enum TileSize
{
    Large,    // 大图标:图标在上,名称在下
    Medium,   // 中等图标:图标在上,名称在下
    Small,    // 小图标:图标在左,名称在右(横向)
    List,     // 列表:图标在左,名称在右(横向)
    Detail,   // 详细信息:图标+名称+修改时间+大小
    Tile,     // 平铺:图标在左,名称在右(卡片,带附加信息)
}
