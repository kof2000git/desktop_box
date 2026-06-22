using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using DesktopBox.Controls;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using DesktopBox.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopBox;

public partial class App : Application
{
    private static Mutex? _mutex;
    public static bool IsShuttingDown { get; private set; }

    /// <summary>全局 DI 容器,供控件/窗口解析服务。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>程序版本号(用于显示,便于确认运行的是哪个构建)。</summary>
    public static string Version { get; } = (typeof(App).Assembly.GetName().Version?.ToString(3)) ?? "1.0";

    public static void BeginShutdown() => IsShuttingDown = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 关键:托盘常驻应用必须用 OnExplicitShutdown。
        // 否则任何对话框/窗口关闭都可能被 WPF 当成"最后一个窗口关闭"而退出程序。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 未处理异常守卫:任何意外都不让进程直接崩(稳定优先);同时落盘日志便于事后排查
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception, "DispatcherUnhandledException");
            try
            {
                var loc = App.Services.GetRequiredService<ILocalizerService>();
                MessageBox.Show(string.Format(loc["dialog.unhandledError"], args.Exception.Message),
                    loc["app.errorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // 后台线程/致命异常(如 0xc0000005 访问违规)会到这里,记录以便事后排查
            LogError(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        // 单实例:防止多开导致配置打架
        _mutex = new Mutex(true, @"Global\DesktopBox_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Services = ConfigureServices();
        MigrateLegacyConfig(); // 便携化:把旧版 %AppData%\DesktopBox 配置一次性搬到 exe 同目录
        base.OnStartup(e);

        // 主题
        var cfg = Services.GetRequiredService<IPersistenceService>().Load();
        var theme = Services.GetRequiredService<IThemeService>();
        if (cfg.Settings.FollowSystemTheme) theme.ApplySystem();
        else theme.Apply(cfg.Settings.Theme);

        // 界面语言:检测系统语言或用手动设置(默认 auto=跟随系统)
        Services.GetRequiredService<ILocalizerService>().Apply(cfg.Settings.Language);

        var main = Services.GetRequiredService<MainWindow>();
        main.Show();
    }

    /// <summary>一次性把旧版 %AppData%\DesktopBox 下的配置搬到可执行文件同目录(仅在目标缺失时复制)。</summary>
    private static void MigrateLegacyConfig()
    {
        try
        {
            var legacyDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopBox"),
                AppContext.BaseDirectory
            };

            foreach (var legacyDir in legacyDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(legacyDir)) continue;

                foreach (var (legacy, current) in new (string, string)[]
                {
                    (Path.Combine(legacyDir, "boxes.json"),    Models.AppPaths.ConfigPath),
                    (Path.Combine(legacyDir, "organize.json"), Models.AppPaths.OrganizePath),
                })
                {
                    if (!File.Exists(legacy) || File.Exists(current)) continue;
                    var dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(legacy, current);
                }
            }
        }
        catch { /* 迁移失败不影响启动 */ }
    }

    /// <summary>把异常(含内部异常链与堆栈)追加写入 AppPaths.LogPath,便于事后排查。</summary>
    public static void LogError(Exception? ex, string source)
    {
        if (ex is null) return;
        try
        {
            var path = Models.AppPaths.LogPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("================================================");
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] source={source}");
            AppendException(sb, ex, 0);
            File.AppendAllText(path, sb.ToString());
        }
        catch { /* 写日志本身失败绝不能影响程序 */ }
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}Type:    {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message: {ex.Message}");
        sb.AppendLine($"{indent}HResult: 0x{ex.HResult:X8}");
        sb.AppendLine($"{indent}StackTrace:");
        sb.AppendLine($"{indent}  {ex.StackTrace?.Trim() ?? "(无)"}");
        if (ex.InnerException is { } inner)
        {
            sb.AppendLine($"{indent}---> InnerException:");
            AppendException(sb, inner, depth + 1);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();
        s.AddSingleton<IPersistenceService>(_ => new JsonStoreService(Models.AppConfig.DefaultPath));
        s.AddSingleton<IDropParserService, DropParserService>();
        s.AddSingleton<IIconExtractorService, IconExtractorService>();
        s.AddSingleton<IThemeService, ThemeService>();
        s.AddSingleton<IStartupService, StartupService>();
        s.AddSingleton<ICategorizerService, CategorizerService>();
        s.AddSingleton<IDesktopScannerService, DesktopScannerService>();
        s.AddSingleton<IOrganizeService, OrganizeService>();
        s.AddSingleton<ILocalizerService, LocalizerService>();
        s.AddSingleton<IDesktopIconsService, DesktopIconsService>();
        s.AddSingleton<IShellChangeNotifierService, ShellChangeNotifierService>();
        s.AddSingleton<FirstLetterKeyboardNavigator>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<MainWindow>();
        s.AddSingleton<SettingsWindow>();
        return s.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
