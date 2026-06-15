using System;
using System.Globalization;
using System.Windows;

namespace DesktopBox.Services;

/// <summary>基于 WPF ResourceDictionary 的本地化实现。
/// 切换语言 = 换 Application.Resources.MergedDictionaries 里的字典源;
/// 所有 {DynamicResource} 绑定会自动刷新(StaticResource 不刷新,故 UI 一律用 DynamicResource)。
/// CS 代码取文本走索引器,每次读取当前字典,不缓存。</summary>
public class LocalizerService : ILocalizerService
{
    // 支持的语言(扩展时在此追加,并新增对应的 Strings.xx.xaml)
    private static readonly string[] Supported = { "zh-CN", "en-US" };

    public string Setting { get; private set; } = "auto";
    public string CurrentLanguage { get; private set; } = "zh-CN";

    public event EventHandler? LanguageChanged;

    public void Apply(string setting)
    {
        Setting = string.IsNullOrEmpty(setting) ? "auto" : setting;
        CurrentLanguage = Setting == "auto" ? DetectSystemLanguage() : ResolveSupported(Setting);

        var app = Application.Current;
        if (app is null)
        {
            // 启动极早期 Application 尚未就绪:仅记录意图,事件照常通知
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        // 仅移除"语言字典"(靠 Source 路径 /Resources/Strings. 识别),保留主题等其它字典
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString;
            if (src is not null && src.Contains("/Resources/Strings.", StringComparison.Ordinal))
                merged.RemoveAt(i);
        }
        merged.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Strings.{CurrentLanguage}.xaml")
        });

        // 诊断:确认检测/解析正确(阶段3 收尾后可移除)
        App.LogError(new Exception(
            $"Localizer applied: setting={Setting} resolved={CurrentLanguage} systemUI={CultureInfo.CurrentUICulture.Name}"),
            "LocalizerService.Apply");

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key]
    {
        get
        {
            var app = Application.Current;
            return (app?.Resources[key] as string) ?? key;
        }
    }

    /// <summary>按系统 UI 文化检测:中文系 → zh-CN,其余 → en-US。</summary>
    private static string DetectSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    }

    /// <summary>把任意设置(如 zh、zh-TW、en-GB)归一到支持列表;无法识别则回退 zh-CN。</summary>
    private static string ResolveSupported(string setting)
    {
        foreach (var s in Supported)
            if (string.Equals(s, setting, StringComparison.OrdinalIgnoreCase)) return s;
        if (setting.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (setting.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return "zh-CN";
    }
}
