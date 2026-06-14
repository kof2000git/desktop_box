using DesktopBox.Models;

namespace DesktopBox.Services;

public interface IOrganizeService
{
    /// <summary>整理后文件存放的根目录。</summary>
    string OrganizedRoot { get; }

    /// <summary>桌面上可被整理的项目数量(预览/确认用)。</summary>
    int CountOrganizable();

    /// <summary>是否存在尚未还原的整理操作。</summary>
    bool HasActiveOrganize { get; }

    /// <summary>执行整理:扫描桌面、分类、移动文件、写清单。返回条目供建盒子;无项目返回 null。</summary>
    OrganizeResult? Organize();

    /// <summary>记录本次整理自动生成的盒子 Id(还原时用于删除)。</summary>
    void RecordBoxIds(IEnumerable<Guid> boxIds);

    /// <summary>还原:把文件移回桌面。返回清单(含 BoxIds);无可还原返回 null。</summary>
    OrganizeResult? Restore();
}
