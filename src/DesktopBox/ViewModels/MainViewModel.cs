using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopBox.Models;
using DesktopBox.Controls;
using DesktopBox.Services;
using DesktopBox.Views;

namespace DesktopBox.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPersistenceService _store;
    private readonly IDropParserService _parser;
    private readonly IIconExtractorService _icon;
    private readonly IOrganizeService _organize;
    private readonly ICategorizerService _categorizer;
    private readonly IDesktopIconsService _desktopIcons;
    private readonly ILocalizerService _localizer;
    private Timer? _debounce;

    public ObservableCollection<BoxViewModel> Boxes { get; } = new();

    /// <summary>用于自动布局新盒子的可用画布宽度(由 MainWindow 设置)。</summary>
    public double ScreenWidth { get; set; } = 1600;

    /// <summary>全局视图模式:所有盒子/标签共享。变化时广播给所有盒子刷新。</summary>
    [ObservableProperty] private ViewMode _globalViewMode = ViewMode.Large;

    // 全局视图模式变化 → 通知所有盒子刷新其 IsLarge/.../IsTile
    partial void OnGlobalViewModeChanged(ViewMode value)
    {
        foreach (var b in Boxes) b.SyncViewMode(value);
        if (value == ViewMode.Detail) EnsureDetailFieldsForAll();
        ScheduleSave();
    }

    /// <summary>切换全局视图模式(由 BoxControl 右键菜单调用)。</summary>
    public void SetGlobalViewMode(ViewMode mode) => GlobalViewMode = mode;

    /// <summary>把新建/加载的盒子加入集合并同步当前全局视图模式。</summary>
    private void AddBoxSynced(BoxViewModel box)
    {
        box.SyncViewMode(GlobalViewMode);
        Boxes.Add(box);
    }

    /// <summary>切到详细信息时惰性填充所有盒子条目的大小/修改时间。</summary>
    private void EnsureDetailFieldsForAll()
    {
        foreach (var b in Boxes) EnsureDetailFields(b);
    }

    // 分类在标签盒子中的排列顺序
    private static readonly string[] CategoryOrderArr =
        { CategorizerService.Program, CategorizerService.Document, CategorizerService.Image,
          CategorizerService.Video, CategorizerService.Audio, CategorizerService.Shortcut,
          CategorizerService.Archive, CategorizerService.FolderCat, CategorizerService.Other };

    public MainViewModel(IPersistenceService store, IDropParserService parser,
                         IIconExtractorService icon, IOrganizeService organize,
                         ICategorizerService categorizer,
                         IDesktopIconsService desktopIcons, ILocalizerService localizer,
                         IShellChangeNotifierService shellChange)
    {
        _store = store;
        _parser = parser;
        _icon = icon;
        _organize = organize;
        _categorizer = categorizer;
        _desktopIcons = desktopIcons;
        _localizer = localizer;
        // 语言切换后刷新所有程序生成盒子/标签的显示名(Header)
        _localizer.LanguageChanged += (_, _) => RefreshAllHeaders();
        // 系统图标自动刷新:回收站清空/还原、此电脑盘符变化等 shell 通知 → 重新提取受影响图标
        shellChange.SystemIconChanged += (_, _) => RefreshSystemIcons();
        // 桌面文件变化:用户在资源管理器删/移桌面文件后,盒子中对应项需同步移除,否则点击失效项报错。
        shellChange.DesktopFilesChanged += (_, _) => PruneStaleItems();
    }

    /// <summary>清理所有盒子(含标签)中"目标已不存在"的条目。用户在资源管理器删桌面文件后触发。
    /// 只移除本地文件类条目(系统图标/URL 不检查,它们有自己的有效性逻辑)。</summary>
    private void PruneStaleItems()
    {
        // 先收集要移除的项(不在遍历中修改集合)
        var stale = new List<BoxItem>();
        foreach (var it in AllItems())
        {
            if (it.Type == ItemType.SystemIcon || it.Type == ItemType.Url) continue;
            if (it.TargetPath.StartsWith("::", StringComparison.Ordinal)) continue;
            if (it.TargetPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(it.TargetPath) && !Directory.Exists(it.TargetPath))
                stale.Add(it);
        }
        if (stale.Count == 0) return;
        foreach (var it in stale) RemoveItemAnywhere(it);
    }

    [RelayCommand]
    private void Load()
    {
        Boxes.Clear();
        var cfg = _store.Load();
        GlobalViewMode = cfg.Settings.GlobalViewMode;

        // 迁移系统图标 item 的旧 CLSID → SystemIcons.cs 当前定义。
        // 历史遗留:旧版用过的 CLSID(如网络曾是 F02C2A56,在 Win11 上已无效)仍存在老 boxes.json 里,
        // 导致图标提取和右键菜单对该项失败(E_INVALIDARG)。按 DisplayName 匹配当前定义并替换。
        var sysClsids = SystemIcons.Definitions
            .ToDictionary(d => d.Name, d => d.Clsid, StringComparer.OrdinalIgnoreCase);
        foreach (var b in cfg.Boxes)
        {
            foreach (var it in b.Items.Concat(b.Tabs.SelectMany(t => t.Items)))
            {
                if (it.Type == ItemType.SystemIcon
                    && sysClsids.TryGetValue(it.DisplayName ?? "", out var clsid)
                    && !string.Equals(it.TargetPath, clsid, StringComparison.OrdinalIgnoreCase))
                {
                    it.TargetPath = clsid;
                    it.IconCachePath = null;   // 清旧图标缓存路径,强制用新 CLSID 重新提取
                }
            }
        }

        // i18n 迁移:旧版程序生成的盒子/标签用中文 Name 当逻辑键。补上 Key,使其按当前语言显示,
        // 且逻辑匹配(找整理盒子/系统图标/分类标签)改用稳定的 Key。仅识别已知的旧中文名。
        foreach (var b in cfg.Boxes)
        {
            if (string.IsNullOrEmpty(b.Key))
            {
                if (b.Name == "桌面整理") b.Key = "box.organize";
                else if (b.Name == "系统图标") b.Key = "box.sysicons";
            }
            foreach (var t in b.Tabs)
            {
                if (string.IsNullOrEmpty(t.Key))
                {
                    if (t.Name == "系统图标") t.Key = "tab.sysicons";
                    else if (CategorizerService.LegacyZhToKey.TryGetValue(t.Name, out var catKey))
                        t.Key = catKey;
                }
            }
        }

        var all = new List<BoxItem>();
        foreach (var b in cfg.Boxes.OrderBy(b => b.Order))
        {
            var vm = new BoxViewModel(b);
            // 启动校正:旧坐标可能在屏外(换分辨率/拔屏幕),拉回可视区,避免"窗口消失找不到"
            var (x, y) = SystemParametersHelper.ClampIntoScreens(vm.X, vm.Y);
            vm.X = x; vm.Y = y;
            AddBoxSynced(vm);
            all.AddRange(vm.Items);
            foreach (var t in vm.Tabs) all.AddRange(t.Items);
        }
        // 图标缓存可能丢失(icons 目录被删/exe 被搬到别的路径):后台重提。
        // Extract 内部有 File.Exists 检查,命中则跳过、缺失则重建。
        if (all.Count > 0) ExtractIconsInBackground(all);
    }

    [RelayCommand]
    private void AddBox()
    {
        AddBoxSynced(new BoxViewModel(new Box
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
        item.Order = box.DisplayItems.Count;   // 标签模式=当前标签,普通模式=Items
        box.DisplayItems.Add(item);
        ExtractIconAsync(item);   // 单个拖入:后台提取,不阻塞
        ScheduleSave();
    }

    /// <summary>从任意盒子(含任意标签)移除一个条目。</summary>
    public void RemoveItemAnywhere(BoxItem item)
    {
        foreach (var box in Boxes)
        {
            if (box.Items.Remove(item)) { ScheduleSave(); return; }
            foreach (var t in box.Tabs)
                if (t.Items.Remove(item)) { ScheduleSave(); return; }
        }
    }

    // ---- 选中态 + 批量操作(跨盒子):所有 BoxItem.IsSelected 汇总 ----

    /// <summary>遍历所有盒子(含标签)的条目。选中操作/批量操作用。</summary>
    public IEnumerable<BoxItem> AllItems()
    {
        foreach (var box in Boxes)
        {
            foreach (var it in box.Items) yield return it;
            foreach (var t in box.Tabs)
                foreach (var it in t.Items) yield return it;
        }
    }

    /// <summary>清除所有选中(点空白/单选新项时调用)。</summary>
    public void ClearSelection()
    {
        foreach (var it in AllItems())
            if (it.IsSelected) it.IsSelected = false;
    }

    /// <summary>处理单个磁贴点击的选中逻辑:普通点击=单选(清其它、选当前),Ctrl=切换(多选)。</summary>
    public void HandleTileClick(BoxItem clicked, bool ctrl)
    {
        if (ctrl)
        {
            clicked.IsSelected = !clicked.IsSelected;
            return;
        }
        // 普通点击:若已有多选,只保留当前;否则正常单选
        var selected = GetSelectedItems();
        if (selected.Count > 1 || (selected.Count == 1 && selected[0] != clicked))
        {
            ClearSelection();
            clicked.IsSelected = true;
        }
        else
        {
            // 当前是唯一选中或都没选:切换
            clicked.IsSelected = !clicked.IsSelected;
        }
    }

    /// <summary>获取所有选中的条目。</summary>
    public List<BoxItem> GetSelectedItems() => AllItems().Where(i => i.IsSelected).ToList();

    /// <summary>批量从盒子移除选中的条目(不删文件本体)。
    /// 按 TargetPath 全删:同一文件可能在盒子多个标签里有重复条目(历史整理遗留),
    /// 只移除选中那条会留下幽灵条目,导致整理时该文件不被当成新文件而无法回来。</summary>
    public void RemoveSelected()
    {
        var selected = GetSelectedItems();
        if (selected.Count == 0) return;
        var paths = selected.Select(i => i.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RemoveItemsByPath(paths);
        ScheduleSave();
    }

    /// <summary>移除盒子中 TargetPath 命中 paths 的所有条目(跨盒子、跨标签)。</summary>
    private void RemoveItemsByPath(HashSet<string> paths)
    {
        foreach (var box in Boxes)
        {
            RemoveMatching(box.Items, paths);
            foreach (var t in box.Tabs) RemoveMatching(t.Items, paths);
        }
    }

    private static void RemoveMatching(System.Collections.Generic.IList<BoxItem> items, HashSet<string> paths)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (paths.Contains(items[i].TargetPath))
                items.RemoveAt(i);
        }
    }

    // ---- 一键整理(增量):只把桌面新文件分类并入「桌面整理」标签盒子 ----
    [RelayCommand]
    private void Organize()
    {
        // 找整理盒子:manifest 指向 → 失效则回退找现有「桌面整理」标签盒子 → 都没有才新建。
        // 回退查找可防止 manifest 丢失/损坏时重复建盒子。
        Guid? knownId = _organize.GetOrganizeBoxId();
        var box = knownId.HasValue ? Boxes.FirstOrDefault(b => b.Id == knownId.Value) : null;
        box ??= Boxes.FirstOrDefault(b => b.Key == "box.organize");
        bool firstTime = box is null;

        if (box is null)
            AddBoxSynced(box = new BoxViewModel(new Box { Key = "box.organize", Name = _localizer["box.organize"], Width = 300, Height = 380, X = 40, Y = 40 }));
        _organize.RecordBoxIds(new[] { box.Id });   // 始终刷新,确保 manifest 指向当前整理盒子

        // 去重:整理盒子历史可能存在同一文件的重复条目(旧版整理逻辑/多次手动拖入),
        // 导致移除一条后该文件仍"存在"于盒子另一标签,整理时无法重新收录("移除后只回来部分")。
        // 整理前按 TargetPath 跨标签去重,每个文件只保留首次出现的条目。
        DedupOrganizeBox(box);

        // 重新归类:分类规则变化(如新增 .py 归文档)后,历史条目可能停在错误的标签。
        // 按当前 Categorizer 规则把每个文件移到正确标签,避免"改了分类旧文件还留在其他标签"。
        ReclassifyOrganizeBox(box);

        // 增量:扫描 → 排除已在整理盒子里的 → 分类并入对应标签
        var scanned = _organize.ScanAndCategorize();
        var existing = new HashSet<string>(
            box.Tabs.SelectMany(t => t.Items).Select(i => i.TargetPath),
            StringComparer.OrdinalIgnoreCase);
        var newEntries = scanned.Where(s => !existing.Contains(s.Path)).ToList();

        var allItems = new List<BoxItem>();
        BoxTab? firstChangedTab = null;   // 第一个收到新文件的标签:整理后切到它,让用户立即看到结果
        foreach (var g in newEntries.GroupBy(e => e.Category).OrderBy(g => CategoryOrder(g.Key)))
        {
            var tab = box.Tabs.FirstOrDefault(t => t.Key == g.Key) ?? NewTab(box, g.Key);
            firstChangedTab ??= tab;
            foreach (var it in g.OrderBy(e => System.IO.Path.GetFileName(e.Path)))
            {
                var item = _parser.ParsePath(it.Path);
                item.BoxId = box.Id;
                item.DisplayName = OrganizeDisplayName(it.Path);
                item.Order = tab.Items.Count;
                tab.Items.Add(item);
                allItems.Add(item);
            }
        }

        // 系统图标标签(首次或缺失时补上,放最后)
        bool sysAdded = EnsureSystemIconsTab(box, allItems);

        // 切到第一个有新文件的标签;无新增时保持首个标签。避免新增文件在其它标签而用户以为"没回来"。
        box.SelectedTab = firstChangedTab ?? box.Tabs.FirstOrDefault();
        Save();
        if (allItems.Count > 0) ExtractIconsInBackground(allItems);

        // 整理后隐藏桌面图标:盒子已收纳这些图标,桌面再显示就重复了。告知用户如何还原显示。
        bool hidIcons = false;
        if (DesktopIconsVisible)
        {
            _desktopIcons.SetVisible(false);
            DesktopIconsVisible = false;
            hidIcons = true;
        }

        string msg;
        if (newEntries.Count == 0)
        {
            msg = firstTime ? _localizer["dialog.organize.empty.first"]
                : (sysAdded ? _localizer["dialog.organize.empty.sysAdded"] : _localizer["dialog.organize.empty.done"]);
        }
        else
        {
            msg = string.Format(_localizer["dialog.organize.done"],
                newEntries.Count,
                sysAdded ? _localizer["dialog.organize.sysIconsTag"] : "");
        }
        if (hidIcons) msg += _localizer["dialog.organize.hidIcons"];
        InputDialog.Inform(msg);
    }

    /// <summary>新建分类标签并加入盒子。catKey 是分类稳定 key(cat.xxx);Name 用当前语言翻译作可读备份。</summary>
    private BoxTab NewTab(BoxViewModel box, string catKey)
    {
        var tab = new BoxTab { Key = catKey, Name = _localizer[catKey] };
        box.Tabs.Add(tab);
        return tab;
    }

    /// <summary>确保整理盒子有「系统图标」标签(此电脑/回收站/控制面板/网络)。已有则不动。</summary>
    private bool EnsureSystemIconsTab(BoxViewModel box, List<BoxItem> extracted)
    {
        if (box.Tabs.Any(t => t.Key == "tab.sysicons")) return false;
        var tab = new BoxTab { Key = "tab.sysicons", Name = _localizer["tab.sysicons"] };
        foreach (var def in SystemIcons.Definitions)
        {
            var item = new BoxItem
            {
                Type = ItemType.SystemIcon,
                TargetPath = def.Clsid,
                DisplayName = def.Name,
                BoxId = box.Id,
                Order = tab.Items.Count
            };
            tab.Items.Add(item);
            extracted.Add(item);
        }
        box.Tabs.Add(tab);
        return true;
    }

    /// <summary>整理时的显示名:.lnk 去扩展名,其余用完整文件名(与旧版一致)。</summary>
    private static string OrganizeDisplayName(string path)
    {
        var n = System.IO.Path.GetFileName(path);
        return n.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetFileNameWithoutExtension(n) : n;
    }

    /// <summary>整理盒子按 TargetPath 跨标签去重:同一文件只保留首次出现的条目。
    /// 系统图标(以 :: 开头)按 TargetPath 也唯一化。修复历史重复条目导致"移除后整理只回来部分"。</summary>
    private static void DedupOrganizeBox(BoxViewModel box)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (var t in box.Tabs)
        {
            for (int i = t.Items.Count - 1; i >= 0; i--)
            {
                var path = t.Items[i].TargetPath;
                if (!seen.Add(path))
                {
                    t.Items.RemoveAt(i);
                    removed++;
                }
            }
        }
        if (removed > 0)
            App.LogError(new Exception($"DedupOrganizeBox removed {removed} duplicate item(s)"), "Organize.dedup");
    }

    /// <summary>按当前分类规则重新归类整理盒子的历史条目。
    /// 分类规则会演进(如新增 .py 归文档),旧条目按当时规则分到了某标签,改规则后不会自动迁移。
    /// 整理时检测"条目当前标签 != 当前分类结果",把不符的移到正确标签。系统图标不动(无扩展名分类)。</summary>
    private void ReclassifyOrganizeBox(BoxViewModel box)
    {
        var moves = new List<(BoxItem item, BoxTab from, string catKey)>();
        foreach (var t in box.Tabs)
        {
            foreach (var it in t.Items)
            {
                if (it.Type == ItemType.SystemIcon) continue;
                if (it.TargetPath.StartsWith("::", StringComparison.Ordinal)) continue;
                var catKey = _categorizer.Categorize(it.TargetPath);
                if (!string.Equals(catKey, t.Key, StringComparison.Ordinal))
                    moves.Add((it, t, catKey));
            }
        }
        foreach (var (item, from, catKey) in moves)
        {
            from.Items.Remove(item);
            var to = box.Tabs.FirstOrDefault(t => t.Key == catKey) ?? NewTab(box, catKey);
            item.Order = to.Items.Count;
            to.Items.Add(item);
        }
        if (moves.Count > 0)
            App.LogError(new Exception($"ReclassifyOrganizeBox moved {moves.Count} item(s)"), "Organize.reclassify");
    }

    private static int CategoryOrder(string c)
    {
        int i = Array.IndexOf(CategoryOrderArr, c);
        return i < 0 ? 99 : i;
    }

    // ---- 系统图标盒子(回收站/此电脑/控制面板)----
    [RelayCommand]
    private void AddSystemIconsBox()
    {
        if (Boxes.Any(b => b.Key == "box.sysicons"))
        {
            InputDialog.Inform(_localizer["dialog.sysIcons.exists"]);
            return;
        }

        var box = new BoxViewModel(new Box { Key = "box.sysicons", Name = _localizer["box.sysicons"], Width = 240, Height = 200 });
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
        AddBoxSynced(box);
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

    // ---- 标签盒子:手动合并 / 拆分 ----

    /// <summary>普通盒子转标签盒子:现有条目移入首个标签。</summary>
    [RelayCommand]
    private void ConvertToTabbed(BoxViewModel? box)
    {
        if (box is null || box.IsTabbed) return;
        var tab = new BoxTab { Name = string.IsNullOrWhiteSpace(box.Name) ? "标签 1" : box.Name };
        foreach (var it in box.Items.ToList())
        {
            it.BoxId = box.Id;
            tab.Items.Add(it);
        }
        box.Items.Clear();
        box.Tabs.Add(tab);
        box.SelectedTab = tab;
        ScheduleSave();
    }

    /// <summary>把 other 合并进 target 作为新标签(普通盒子或标签盒子均可)。</summary>
    public void MergeIn(BoxViewModel target, BoxViewModel other)
    {
        if (target.Id == other.Id) return;

        if (other.IsTabbed)
        {
            foreach (var t in other.Tabs.ToList())
            {
                var nt = new BoxTab { Name = t.Name };
                foreach (var it in t.Items.ToList()) { it.BoxId = target.Id; nt.Items.Add(it); }
                target.Tabs.Add(nt);
            }
        }
        else
        {
            var nt = new BoxTab { Name = string.IsNullOrWhiteSpace(other.Name) ? "标签" : other.Name };
            foreach (var it in other.Items.ToList()) { it.BoxId = target.Id; nt.Items.Add(it); }
            target.Tabs.Add(nt);
        }

        target.SelectedTab = target.Tabs.LastOrDefault();
        Boxes.Remove(other);
        ScheduleSave();
    }

    /// <summary>标签盒子拆分为独立盒子:每个标签变成一个盒子,位置略有错开。</summary>
    [RelayCommand]
    private void SplitBox(BoxViewModel? box)
    {
        if (box is null || !box.IsTabbed) return;
        if (!InputDialog.Confirm(string.Format(_localizer["dialog.split.confirm"], box.Header, box.Tabs.Count)))
            return;

        int index = Boxes.IndexOf(box);
        double baseX = box.X, baseY = box.Y;
        var created = new List<BoxViewModel>();
        int i = 0;
        foreach (var tab in box.Tabs.ToList())
        {
            var nb = new BoxViewModel(new Box
            {
                Name = tab.Name,
                X = baseX + (i % 4) * 30,
                Y = baseY + (i / 4) * 30,
                Width = box.Width,
                Height = box.Height
            });
            foreach (var it in tab.Items.ToList()) { it.BoxId = nb.Id; nb.Items.Add(it); }
            created.Add(nb);
            i++;
        }

        Boxes.Remove(box);
        for (int j = 0; j < created.Count; j++)
        {
            created[j].SyncViewMode(GlobalViewMode);
            Boxes.Insert(index + j, created[j]);
        }
        ScheduleSave();
    }

    // ---- 桌面图标显隐(纯视觉,不动文件)----
    /// <summary>当前桌面图标是否可见(可观察:按钮颜色据此变化)。</summary>
    [ObservableProperty] private bool _desktopIconsVisible = true;

    [RelayCommand]
    private void ToggleDesktopIcons()
    {
        var visible = !DesktopIconsVisible;
        _desktopIcons.SetVisible(visible);
        DesktopIconsVisible = visible;
    }

    /// <summary>启动后读取真实状态,刷新按钮颜色。</summary>
    public void RefreshDesktopIconsState() => DesktopIconsVisible = _desktopIcons.AreIconsVisible;

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

    /// <summary>收到 shell 变化通知(回收站清空/还原、系统图标列表更新等)后,
    /// 重新提取所有系统图标(回收站空/满、此电脑盘符等状态会变)。forceRefresh=true 强制删旧缓存。</summary>
    private void RefreshSystemIcons()
    {
        // 跨所有盒子 + 标签收集系统图标(TargetPath 以 :: 开头)
        var sysItems = new List<BoxItem>();
        foreach (var b in Boxes)
        {
            var src = b.IsTabbed ? b.Tabs.SelectMany(t => t.Items) : b.Items;
            foreach (var it in src)
            {
                if (it.TargetPath.StartsWith("::", StringComparison.Ordinal))
                    sysItems.Add(it);
            }
        }
        if (sysItems.Count == 0) return;

        Task.Run(() =>
        {
            foreach (var it in sysItems)
            {
                try
                {
                    var icon = _icon.Extract(it.TargetPath, forceRefresh: true);
                    var disp = Application.Current?.Dispatcher;
                    if (disp is null || disp.HasShutdownStarted) continue;
                    disp.BeginInvoke(new Action(() => it.IconCachePath = icon));
                }
                catch (Exception ex) { App.LogError(ex, "RefreshSystemIcons.Extract"); }
            }
        });
    }

    /// <summary>为详细信息视图惰性填充每个条目的大小/修改时间(后台线程,逐个回 UI 更新)。</summary>
    public void EnsureDetailFields(BoxViewModel box)
    {
        // 收集所有条目:标签模式要覆盖每个标签(不只当前选中标签)——否则切到其它标签时,
        // 那些标签的 ModifiedText 仍是 null,详细信息视图里就不显示修改时间(“有的标签有,有的没有”)。
        var source = box.IsTabbed ? box.Tabs.SelectMany(t => t.Items) : box.Items;
        var items = source.Where(i => i.ModifiedText is null).ToList();
        if (items.Count == 0) return;
        Task.Run(() =>
        {
            foreach (var it in items)
            {
                string? size = null, mod = null;
                try
                {
                    // 系统图标/网址等没有文件实体,显示占位
                    if (it.Type == ItemType.SystemIcon || it.TargetPath.StartsWith("::") ||
                        it.Type == ItemType.Url || it.TargetPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        size = "—"; mod = "—";
                    }
                    else if (Directory.Exists(it.TargetPath))
                    {
                        size = "—"; // 文件夹不显示大小
                        mod = new DirectoryInfo(it.TargetPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                    else if (File.Exists(it.TargetPath))
                    {
                        var fi = new FileInfo(it.TargetPath);
                        size = FormatSize(fi.Length);
                        mod = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                }
                catch { }

                var disp = Application.Current?.Dispatcher;
                if (disp is null || disp.HasShutdownStarted) continue;
                var s = size; var m = mod;
                disp.BeginInvoke(new Action(() => { it.SizeText = s; it.ModifiedText = m; }));
            }
        });
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
    };

    // ---- 持久化 ----
    public void ScheduleSave()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp is null || disp.HasShutdownStarted)
                {
                    Save();
                    return;
                }
                disp.BeginInvoke(new Action(() =>
                {
                    try { Save(); }
                    catch { /* 落盘失败不崩 */ }
                }));
            }
            catch { /* 落盘失败不崩 */ }
        }, null, 300, Timeout.Infinite);
    }

    public void Save()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp is not null && !disp.CheckAccess())
        {
            if (disp.HasShutdownStarted) return;
            try
            {
                disp.Invoke(new Action(Save));
            }
            catch { /* 落盘失败不应影响使用 */ }
            return;
        }

        try
        {
            var existing = _store.Load();
            var cfg = new AppConfig { Settings = existing.Settings };
            cfg.Settings.GlobalViewMode = GlobalViewMode;
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

    /// <summary>语言切换后:刷新所有盒子与标签的 Header(显示名),让程序生成的项按新语言显示。</summary>
    private void RefreshAllHeaders()
    {
        foreach (var b in Boxes)
        {
            b.RefreshHeader();
            foreach (var t in b.Tabs) t.RefreshHeader();
        }
    }
}
