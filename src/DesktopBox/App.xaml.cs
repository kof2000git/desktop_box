using System;
using System.Threading;
using System.Windows;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using DesktopBox.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox;

public partial class App : Application
{
    private static Mutex? _mutex;

    /// <summary>全局 DI 容器,供控件/窗口解析服务。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例:防止多开导致配置打架
        _mutex = new Mutex(true, @"Global\DesktopBox_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Services = ConfigureServices();
        base.OnStartup(e);

        // 主题
        var cfg = Services.GetRequiredService<IPersistenceService>().Load();
        var theme = Services.GetRequiredService<IThemeService>();
        if (cfg.Settings.FollowSystemTheme) theme.ApplySystem();
        else theme.Apply(cfg.Settings.Theme);

        // 主窗口
        var main = Services.GetRequiredService<MainWindow>();
        main.Show();

        // 贴桌面层;失败则降级为置顶窗口,绝不崩
        if (!Services.GetRequiredService<IDesktopService>().AttachToDesktop(main))
            main.Topmost = true;
    }

    private static IServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();
        s.AddSingleton<IPersistenceService>(_ => new JsonStoreService(Models.AppConfig.DefaultPath));
        s.AddSingleton<IDropParserService, DropParserService>();
        s.AddSingleton<IIconExtractorService, IconExtractorService>();
        s.AddSingleton<IDesktopService, DesktopLayerService>();
        s.AddSingleton<IThemeService, ThemeService>();
        s.AddSingleton<IStartupService, StartupService>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<MainWindow>();
        s.AddSingleton<SettingsWindow>();
        return s.BuildServiceProvider();
    }
}
