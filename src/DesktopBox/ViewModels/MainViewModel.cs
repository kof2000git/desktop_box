using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopBox.Models;
using DesktopBox.Services;

namespace DesktopBox.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPersistenceService _store;
    private readonly IDropParserService _parser;
    private readonly IIconExtractorService _icon;
    private Timer? _debounce;

    public ObservableCollection<BoxViewModel> Boxes { get; } = new();

    public MainViewModel(IPersistenceService store, IDropParserService parser, IIconExtractorService icon)
    {
        _store = store;
        _parser = parser;
        _icon = icon;
    }

    [RelayCommand]
    private void Load()
    {
        Boxes.Clear();
        var cfg = _store.Load();
        foreach (var b in cfg.Boxes.OrderBy(b => b.Order))
            Boxes.Add(new BoxViewModel(b));
    }

    [RelayCommand]
    private void AddBox()
    {
        Boxes.Add(new BoxViewModel(new Box
        {
            Name = $"盒子 {Boxes.Count + 1}",
            Order = Boxes.Count
        }));
        ScheduleSave();
    }

    [RelayCommand]
    private void RemoveBox(BoxViewModel? box)
    {
        if (box is null) return;
        Boxes.Remove(box);
        ScheduleSave();
    }

    /// <summary>把一个路径或网址加入指定盒子:解析类型、提取图标、加入集合、防抖保存。</summary>
    public void AddItemToBox(BoxViewModel box, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        var item = target.StartsWith("http", System.StringComparison.OrdinalIgnoreCase)
            ? _parser.ParseUrl(target)
            : _parser.ParsePath(target);

        item.BoxId = box.Id;
        item.Order = box.Items.Count;
        item.IconCachePath = _icon.Extract(item.TargetPath);
        box.Items.Add(item);
        ScheduleSave();
    }

    public void RemoveItem(BoxViewModel box, BoxItem item)
    {
        box.Items.Remove(item);
        ScheduleSave();
    }

    /// <summary>防抖保存:300ms 内多次变更只落盘一次。</summary>
    public void ScheduleSave()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            try { Save(); }
            catch { /* 落盘失败不崩,下次再试 */ }
        }, null, 300, Timeout.Infinite);
    }

    public void Save()
    {
        var existing = _store.Load();
        var cfg = new AppConfig { Settings = existing.Settings };
        int order = 0;
        foreach (var b in Boxes)
        {
            var m = b.ToModel();
            m.Order = order++;
            cfg.Boxes.Add(m);
        }
        _store.Save(cfg);
    }
}
