namespace DesktopBox.Services;

public interface IOrganizeService
{
    /// <summary>是否已有整理盒子记录(organize.json 存在)。</summary>
    bool HasActiveOrganize { get; }

    /// <summary>桌面可整理条目总数(提示用)。</summary>
    int CountOrganizable();

    /// <summary>扫描桌面并按类型分类,返回 (完整路径, 分类名) 列表。不写任何记录。</summary>
    List<(string Path, string Category)> ScanAndCategorize();

    /// <summary>记录整理盒子的 id(标识哪个盒子是自动整理的,便于增量复用)。</summary>
    void RecordBoxIds(IEnumerable<Guid> boxIds);

    /// <summary>读取整理盒子 id;无记录返回 null。</summary>
    Guid? GetOrganizeBoxId();
}
