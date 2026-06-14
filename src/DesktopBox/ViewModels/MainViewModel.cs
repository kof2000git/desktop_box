using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.Views;

namespace DesktopBox.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPersistenceService _store;
    private readonly IDropParserService _parser;
    private readonly IIconExtractorService _icon;
    private readonly IOrganizeService _organize;
    private readonly IDesktopIconsService _desktopIcons;
    private Timer? _debounce;

    public ObservableCollection<BoxViewModel> Boxes { get; } = new();

    /// <summary>用于自动布局新盒子的可用画布宽度(由 MainWindow 设置)。</summary>
    public double ScreenWidth { get; set; } = 1600;

    public MainViewModel(IPersistenceService store, IDropParserService parser,
                         IIconExtractorService icon, IOrganizeService organize,
                         IDesktopIconsService desktopIcons)
    {
        _store = store;
        _parser = parser;
        _icon = icon;
        _organize = organize;
        _desktopIcons = desktopIcons;
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

    public void AddItemToBox(BoxViewModel box, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        var item = target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? _parser.ParseUrl(target)
            : _parser.ParsePath(target);

        item.BoxId = box.Id;
        item.Order = box.Items.Count;
        box.Items.Add(item);
        ExtractIconAsync(item);   // 单个拖入:后台提取,不阻塞
        ScheduleSave();
    }

    public void RemoveItem(BoxViewModel box, BoxItem item)
    {
        box.Items.Remove(item);
        ScheduleSave();
    }

    // ---- 一键整理(只引用、不移动)----
    [RelayCommand]
    private void Organize()
    {
        if (_organize.HasActiveOrganize)
        {
            InputDialog.Inform("已存在一次整理操作。请先「还原整理」后再整理。");
            return;
        }

        int count = _organize.CountOrganizable();
        if (count == 0)
        {
            InputDialog.Inform("桌面上没有需要整理的项目。");
            return;
        }

        if (!InputDialog.Confirm(
            $"检测到桌面有 {count} 个项目。\n\n" +
            "将自动按类型(程序/文档/图片/压缩包/视频/音乐/文件夹/其他)分类," +
            "为每个分类自动创建盒子,并【引用】桌面上的文件。\n\n" +
            "✅ 文件不会被移动,写死桌面路径的其它程序/脚本照常工作。\n\n" +
            "是否继续?\n(可用「还原整理」删除这些盒子)"))
            return;

        var result = _organize.Organize();
        if (result is null || result.Entries.Count == 0)
        {
            InputDialog.Inform("没有可整理的项目。");
            return;
        }

        var newBoxes = new List<BoxViewModel>();
        var allItems = new List<BoxItem>();
        foreach (var g in result.Entries.GroupBy(e => e.Category).OrderBy(g => g.Key))
        {
            var box = new BoxViewModel(new Box { Name = g.Key, Width = 240, Height = 320 });
            foreach (var it in g.OrderBy(e => e.DisplayName))
            {
                var item = _parser.ParsePath(it.CurrentPath);
                item.BoxId = box.Id;
                item.DisplayName = it.DisplayName;
                item.Order = box.Items.Count;
                box.Items.Add(item);
                allItems.Add(item);
            }
            newBoxes.Add(box);
            Boxes.Add(box);
        }

        LayoutNewBoxes(newBoxes);
        _organize.RecordBoxIds(newBoxes.Select(b => b.Id));
        Save();

        InputDialog.Inform($"整理完成!已创建 {newBoxes.Count} 个盒子,共 {result.Entries.Count} 个项目。\n(文件未移动,图标稍候加载。)\n\n想让桌面更干净?可用「隐藏/显示桌面图标」隐藏原始图标。");

        ExtractIconsInBackground(allItems);
    }

    [RelayCommand]
    private void RestoreOrganize()
    {
        if (!_organize.HasActiveOrganize)
        {
            InputDialog.Inform("没有可还原的整理操作。");
            return;
        }

        if (!InputDialog.Confirm("将删除这次自动生成的分类盒子。\n(文件从未移动,无需移回;手动建的盒子不受影响)\n是否继续?"))
            return;

        var result = _organize.Restore();
        if (result?.Manifest?.BoxIds is { Count: > 0 } ids)
        {
            foreach (var b in Boxes.Where(b => ids.Contains(b.Id)).ToList())
                Boxes.Remove(b);
        }
        Save();

        InputDialog.Inform("已删除自动生成的分类盒子。文件仍在桌面原处。");
    }

    // ---- 系统图标盒子(回收站/此电脑/控制面板)----
    [RelayCommand]
    private void AddSystemIconsBox()
    {
        if (Boxes.Any(b => b.Name == "系统图标"))
        {
            InputDialog.Inform("已经存在「系统图标」盒子了。");
            return;
        }

        var box = new BoxViewModel(new Box { Name = "系统图标", Width = 240, Height = 200 });
        var items = new List<BoxItem>();
        foreach (var def in SystemIcons.Definitions)
        {
            var item = new BoxItem
            {
                Type = ItemType.SystemIcon,
                TargetPath = def.Clsid,
                DisplayName = def.Name,
                BoxId = box.Id,
                Order = box.Items.Count
            };
            box.Items.Add(item);
            items.Add(item);
        }

        PlaceBoxAtNextSlot(box);
        Boxes.Add(box);
        Save();
        ExtractIconsInBackground(items);
    }

    private void PlaceBoxAtNextSlot(BoxViewModel box)
    {
        int idx = Boxes.Count;
        int cols = Math.Max(1, (int)((ScreenWidth - 40) / 260));
        box.X = 40 + (idx % cols) * 260;
        box.Y = 40 + (idx / cols) * 340;
    }

    // ---- 桌面图标显隐(纯视觉,不动文件)----
    [RelayCommand]
    private void ToggleDesktopIcons()
    {
        var visible = !_desktopIcons.AreIconsVisible;
        _desktopIcons.SetVisible(visible);
        InputDialog.Inform(visible
            ? "已显示桌面图标。"
            : "已隐藏桌面图标。\n(文件没有移动,只是视觉隐藏;再次点击可恢复)");
    }

    private void LayoutNewBoxes(List<BoxViewModel> newBoxes)
    {
        int cols = Math.Max(1, (int)((ScreenWidth - 40) / 260));
        for (int i = 0; i < newBoxes.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            newBoxes[i].X = 40 + col * 260;
            newBoxes[i].Y = 40 + row * 340;
        }
    }

    /// <summary>后台批量提取图标,逐个回到 UI 线程更新(BoxItem.IconCachePath 可观察,磁贴自动刷新)。</summary>
    private void ExtractIconsInBackground(List<BoxItem> items)
    {
        Task.Run(() =>
        {
            foreach (var it in items)
            {
                try
                {
                    var icon = _icon.Extract(it.TargetPath);
                    var disp = Application.Current?.Dispatcher;
                    if (disp is null || disp.HasShutdownStarted) continue;
                    disp.BeginInvoke(new Action(() => it.IconCachePath = icon));
                }
                catch { /* 程序退出中或其他异常,忽略 */ }
            }
        });
    }

    private void ExtractIconAsync(BoxItem item) => ExtractIconsInBackground(new List<BoxItem> { item });

    // ---- 持久化 ----
    public void ScheduleSave()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            try { Save(); }
            catch { /* 落盘失败不崩 */ }
        }, null, 300, Timeout.Infinite);
    }

    public void Save()
    {
        try
        {
            var existing = _store.Load();
            var cfg = new AppConfig { Settings = existing.Settings };
            int order = 0;
            // 快照:避免后台线程枚举时集合被 UI 线程修改
            foreach (var b in Boxes.ToList())
            {
                var m = b.ToModel();
                m.Order = order++;
                cfg.Boxes.Add(m);
            }
            _store.Save(cfg);
        }
        catch { /* 落盘失败不应影响使用,也不弹吓人错误框 */ }
    }
}
