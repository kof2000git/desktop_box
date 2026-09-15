using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
    public static void CancelShutdown() => IsShuttingDown = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 关键:托盘常驻应用必须用 OnExplicitShutdown。
        // 否则任何对话框/窗口关闭都可能被 WPF 当成"最后一个窗口关闭"而退出程序。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        global::DesktopBox.Services.LogService.WriteStartMarker();
        global::DesktopBox.Services.WpfUiAnimationGuard.EnsureRegistered();

        // 未处理异常守卫:任何意外都不让进程直接崩(稳定优先);同时落盘日志便于事后排查
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception, "DispatcherUnhandledException");
            var canContinue = CanContinueAfterDispatcherException(args.Exception)
                || IsBenignAnimationFailure(args.Exception);
            args.Handled = canContinue;
            try
            {
                var loc = App.Services.GetRequiredService<ILocalizerService>();
                MessageBox.Show(string.Format(loc[GetDispatcherExceptionMessageKey(args.Exception)], args.Exception.Message),
                    loc["app.errorTitle"], MessageBoxButton.OK,
                    canContinue ? MessageBoxImage.Warning : MessageBoxImage.Error);
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // 后台线程/致命异常(如 0xc0000005 访问违规)会到这里,记录以便事后排查
            LogError(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };

        // 单实例:防止多开导致配置打架。Local\ 无需特权,受限账户/多会话也可用。
        // 若连 Local\ 都建失败则记录后继续运行(宁可多开,不启动即崩)。
        try
        {
            _mutex = new Mutex(true, @"Local\DesktopBox_SingleInstance", out var createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "App.SingleInstance");
            _mutex = null;
        }

        Services = ConfigureServices();
        MigrateLegacyConfig(); // 便携化:把旧版 %AppData%\DesktopBox 配置一次性搬到 exe 同目录
        base.OnStartup(e);

        try
        {
            // 主题
            var cfg = Services.GetRequiredService<IPersistenceService>().Load();
            var theme = Services.GetRequiredService<IThemeService>();
            if (cfg.Settings.FollowSystemTheme) theme.ApplySystem();
            else theme.Apply(cfg.Settings.Theme);

            // 界面语言:检测系统语言或用手动设置(默认 auto=跟随系统)
            Services.GetRequiredService<ILocalizerService>().Apply(cfg.Settings.Language);

            // 开机自启:配置要求开启时重写 Run 项(修正迁移/升级后路径失效);已注册则刷新当前 exe 路径。
            try
            {
                var startup = Services.GetRequiredService<IStartupService>();
                if (cfg.Settings.AutoStart || startup.IsEnabled())
                    startup.Enable();
            }
            catch (Exception ex)
            {
                LogError(ex, "App.EnsureAutoStart");
            }

            var main = Services.GetRequiredService<MainWindow>();
            main.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError(ex, "App.StartupConfiguration");
            try
            {
                var loc = Services.GetRequiredService<ILocalizerService>();
                MessageBox.Show(string.Format(loc["dialog.startupConfigFailed"], ex.Message),
                    loc["app.errorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            Shutdown();
        }
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
        try { global::DesktopBox.Services.LogService.Error(ex, source); } catch { }
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
        s.AddSingleton<IShellMenuRunner, ShellMenuProcessRunner>();
        s.AddSingleton<FirstLetterKeyboardNavigator>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<MainWindow>();
        s.AddSingleton<SettingsWindow>();
        return s.BuildServiceProvider();
    }

    internal static bool CanContinueAfterDispatcherException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        COMException or
        Win32Exception or
        OperationCanceledException;

    /// <summary>
    /// 良性动画失败兜底（纵深防御）：第三方样式（WPF-UI hover 动画）对 frozen 画刷跑
    /// Storyboard 会抛 InvalidOperationException。动画播不出来不影响任何状态，
    /// WPF 后续渲染照常，允许继续运行；其它 InvalidOperationException 仍按致命处理。
    /// 根因修复见 ThemeBrushUnfreezer（这里只是防漏网）。
    /// </summary>
    internal static bool IsBenignAnimationFailure(Exception exception) =>
        exception is InvalidOperationException
        && exception.StackTrace?.Contains("System.Windows.Media.Animation") == true;

    internal static string GetDispatcherExceptionMessageKey(Exception exception) =>
        CanContinueAfterDispatcherException(exception) ? "dialog.unhandledError" : "dialog.fatalError";

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }
}
