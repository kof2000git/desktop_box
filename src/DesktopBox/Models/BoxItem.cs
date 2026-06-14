using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopBox.Models;

/// <summary>盒内单个条目。IconCachePath 设为可观察,以便后台异步提取图标后实时刷新磁贴。</summary>
public partial class BoxItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BoxId { get; set; }
    public ItemType Type { get; set; }
    public string TargetPath { get; set; } = "";
    public string DisplayName { get; set; } = "";

    [ObservableProperty] private string? _iconCachePath;

    public int Order { get; set; }
}
