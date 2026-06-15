using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBox.Models;
using DesktopBox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox.ViewModels;

public partial class BoxViewModel : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty] private string _name;
    /// <summary>程序生成盒子的稳定标识(box.organize / box.sysicons);用户手建为 null。逻辑匹配用 Key。</summary>
    public string? Key { get; }
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private string? _accentColor;

    public ObservableCollection<BoxItem> Items { get; } = new();
    public ObservableCollection<BoxTab> Tabs { get; } = new();

    [ObservableProperty] private BoxTab? _selectedTab;

    /// <summary>是否为标签模式(Tabs 非空)。</summary>
    public bool IsTabbed => Tabs.Count > 0;

    /// <summary>当前盒子在 UI 上应渲染的条目集合:普通模式=Items,标签模式=选中标签的 Items。</summary>
    public ObservableCollection<BoxItem> DisplayItems =>
        IsTabbed && SelectedTab is not null ? SelectedTab.Items : Items;

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

    // ---- 视图模式:统一用全局(MainViewModel.GlobalViewMode),所有盒子/标签一致 ----
    // ViewMode 字段保留为"当前视图"的只读镜像,IsLarge 等基于它;由 MainViewModel 广播刷新。
    public ViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            OnPropertyChanged(nameof(ViewMode));
            RefreshViewMode();
        }
    }
    private ViewMode _viewMode = ViewMode.Large;

    public bool IsLarge => ViewMode == ViewMode.Large;
    public bool IsMedium => ViewMode == ViewMode.Medium;
    public bool IsSmall => ViewMode == ViewMode.Small;
    public bool IsList => ViewMode == ViewMode.List;
    public bool IsDetail => ViewMode == ViewMode.Detail;
    public bool IsTile => ViewMode == ViewMode.Tile;

    /// <summary>同步全局模式到本盒子,并刷新所有视图相关属性(由 MainViewModel.GlobalViewMode 变化时调用)。</summary>
    public void SyncViewMode(ViewMode global)
    {
        _viewMode = global;
        OnPropertyChanged(nameof(ViewMode));
        RefreshViewMode();
    }

    /// <summary>重新计算并通知所有视图模式相关属性。</summary>
    public void RefreshViewMode()
    {
        OnPropertyChanged(nameof(IsLarge));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsSmall));
        OnPropertyChanged(nameof(IsList));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsTile));
        ViewModeChanged?.Invoke(this);
    }

    /// <summary>视图模式变更时触发(供 BoxControl 在切到详细信息时惰性填充大小/时间)。</summary>
    public event Action<BoxViewModel>? ViewModeChanged;

    /// <summary>条目总数(跨所有标签)。用于判断盒子是否允许删除。</summary>
    public int TotalItemCount =>
        IsTabbed ? Tabs.Sum(t => t.Items.Count) : Items.Count;

    public BoxViewModel(Box model)
    {
        Id = model.Id;
        _name = model.Name;
        Key = model.Key;
        _x = model.X; _y = model.Y;
        _width = model.Width; _height = model.Height;
        _accentColor = model.AccentColor;
        _viewMode = model.ViewMode;
        foreach (var i in model.Items) Items.Add(i);
        foreach (var t in model.Tabs) Tabs.Add(t);
        if (Tabs.Count > 0) _selectedTab = Tabs[0];

        // 增删标签后通知 IsTabbed / DisplayItems,并自动选首个标签
        Tabs.CollectionChanged += OnTabsChanged;
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsTabbed));
        OnPropertyChanged(nameof(DisplayItems));
        if (SelectedTab is null || !Tabs.Contains(SelectedTab))
            SelectedTab = Tabs.FirstOrDefault();
    }

    partial void OnSelectedTabChanged(BoxTab? value)
        => OnPropertyChanged(nameof(DisplayItems));

    /// <summary>清空所有条目(普通模式清 Items,标签模式清每个标签)。</summary>
    public void ClearAll()
    {
        Items.Clear();
        foreach (var t in Tabs) t.Items.Clear();
    }

    /// <summary>新建一个空标签并选中它。name 为空时用当前语言的默认标签名。</summary>
    public BoxTab AddTab(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = $"{App.Services.GetRequiredService<ILocalizerService>()["tab.default"]} {Tabs.Count + 1}";
        var tab = new BoxTab { Name = name };
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    public Box ToModel() => new()
    {
        Id = Id,
        Name = Name,
        Key = Key,
        X = X, Y = Y,
        Width = Width, Height = Height,
        AccentColor = AccentColor,
        ViewMode = ViewMode,
        Items = new(Items),
        Tabs = new(Tabs)
    };
}
