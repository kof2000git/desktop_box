using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopBox.Models;

public class Box
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新盒子";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 200;
    public string? AccentColor { get; set; }
    public int Order { get; set; }
    public ViewMode ViewMode { get; set; } = ViewMode.Large;
    public List<BoxItem> Items { get; set; } = new();

    /// <summary>标签模式:非空时盒子显示为多标签页(Tabs 优先于 Items)。</summary>
    public List<BoxTab> Tabs { get; set; } = new();
}

/// <summary>标签盒子里的一个标签页。Name 可观察(重命名时 UI 自动刷新)。</summary>
public partial class BoxTab : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _name = "标签";
    public ObservableCollection<BoxItem> Items { get; set; } = new();
}
