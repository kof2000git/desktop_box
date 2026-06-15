using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox.Models;

public class Box
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新盒子";
    /// <summary>程序生成盒子/标签的稳定标识(如 box.organize / cat.apps);用户手建为 null。
    /// 显示走 Header(有 Key→当前语言翻译,无 Key→Name);逻辑匹配用 Key(不随语言变)。</summary>
    public string? Key { get; set; }
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
    /// <summary>程序生成标签的稳定标识(如 cat.apps / tab.sysicons);用户手建为 null。</summary>
    public string? Key { get; set; }
    public ObservableCollection<BoxItem> Items { get; set; } = new();

    /// <summary>显示名:有 Key 时取当前语言翻译,否则用 Name。语言切换时由 MainViewModel 触发刷新。</summary>
    public string Header
    {
        get
        {
            if (!string.IsNullOrEmpty(Key))
            {
                var loc = App.Services.GetRequiredService<ILocalizerService>();
                return loc[Key];
            }
            return Name;
        }
    }

    /// <summary>语言切换后重算 Header(触发 PropertyChanged)。</summary>
    public void RefreshHeader() => OnPropertyChanged(nameof(Header));
}
