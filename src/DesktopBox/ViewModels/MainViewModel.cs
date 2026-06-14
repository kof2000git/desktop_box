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
    private Timer? _debounce;

    public ObservableCollection<BoxViewModel> Boxes { get; } = new();

    /// <summary>用于自动布局新盒子的可用画布宽度(由 MainWindow 设置)。</summary>
    public double ScreenWidth { get; set; } = 1600;

    public MainViewModel(IPersistenceService store, IDropParserService parser,
                         IIconExtractorService icon, IOrganizeService organize)
    {
        _store = store;
        _parser = parser;
        _icon = icon;
        _organize = organize;
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

    // ---- 一键整理 ----
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
            "将自动按类型分类(程序/文档/图片/压缩包/视频/音乐/文件夹/其他)," +
            "为每个分类自动创建盒子,并把文件【移动】到:\n" +
            $"{_organize.OrganizedRoot}\n\n" +
            "(与桌面同盘,瞬间完成;可用「还原整理」一键撤销)\n是否继续?"))
            return;

        var result = _organize.Organize();
        if (result is null || result.Entries.Count == 0)
        {
            InputDialog.Inform("没有可整理的项目(可能文件被占用)。");
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

        InputDialog.Inform($"整理完成!已创建 {newBoxes.Count} 个盒子,共 {result.Entries.Count} 个项目。\n图标正在后台加载,稍候即会显示。");

        // 后台提取图标:不阻塞整理流程
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

        if (!InputDialog.Confirm("将把所有已整理的项目移回桌面,并删除自动生成的盒子。\n是否继续?"))
            return;

        var result = _organize.Restore();
        if (result?.Manifest?.BoxIds is { Count: > 0 } ids)
        {
            foreach (var b in Boxes.Where(b => ids.Contains(b.Id)).ToList())
                Boxes.Remove(b);
        }
        Save();

        InputDialog.Inform("已还原:文件已移回桌面,自动生成的盒子已删除。");
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
