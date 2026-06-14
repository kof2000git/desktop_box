namespace DesktopBox.Services;

/// <summary>常见桌面系统图标(Shell 虚拟项)的定义。仅保留实测可解析的 CLSID。</summary>
public static class SystemIcons
{
    public readonly record struct Def(string Name, string Clsid);

    public static readonly Def[] Definitions =
    {
        new("此电脑",   "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
        new("回收站",   "::{645FF040-5081-101B-9F08-00AA002F954E}"),
        new("控制面板", "::{21EC2020-3AEA-1069-A2DD-08002B30309D}"),
    };
}
