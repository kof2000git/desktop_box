namespace DesktopBox.Services;

/// <summary>本地化服务:管理界面语言(系统检测 / 手动切换),并提供 CS 代码取本地化文本的入口。</summary>
public interface ILocalizerService
{
    /// <summary>用户设置值:"auto"(跟随系统)/ "zh-CN" / "en-US"。</summary>
    string Setting { get; }

    /// <summary>当前实际生效的语言(解析 auto 后的 zh-CN/en-US)。</summary>
    string CurrentLanguage { get; }

    /// <summary>应用语言。setting="auto" 时按系统 UI 文化检测,否则用指定语言。
    /// 切换会即时刷新所有 DynamicResource(WPF 换 ResourceDictionary 源即触发)。</summary>
    void Apply(string setting);

    /// <summary>按 key 取当前语言的文本(找不到回退 key 本身,避免界面空白)。</summary>
    string this[string key] { get; }

    /// <summary>语言切换后触发:供 ViewModel 重算"由代码生成的本地化文本"
    /// (如分类标签名这类随语言变的动态文本)。</summary>
    event EventHandler? LanguageChanged;
}
