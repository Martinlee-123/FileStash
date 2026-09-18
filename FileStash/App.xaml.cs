using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FileStash;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>单实例互斥体：防止重复启动导致多个托盘图标 / 任务栏闪图标。</summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>崩溃日志路径：%AppData%\FileStash\crash.log</summary>
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileStash", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- 单实例检查 ----
        // createdNew=false 表示已有实例在运行（可能处于未响应状态，但互斥体仍被其持有）。
        _singleInstanceMutex = new Mutex(true, @"Global\FileStash_SingleInstance_v1", out bool createdNew);
        if (!createdNew)
        {
            // 已有实例：静默退出，绝不创建任何窗口（否则任务栏会闪一下图标）
            Shutdown();
            return;
        }

        // ---- 全局异常兜底：不让偶发异常直接杀死整个程序 ----
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogException("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("Task", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        // 手动创建主窗口（替代 StartupUri）
        var window = new MainWindow();
        window.Show();
    }

    /// <summary>UI 线程未处理异常：记录日志后吞掉，保证托盘工具尽量存活（而不是直接崩退）。</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher", e.Exception);
        e.Handled = true;
    }

    /// <summary>把异常写入 crash.log（追加，带时间戳与堆栈）。写日志本身失败也绝不再抛。</summary>
    public static void LogException(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\r\n\r\n";
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
            // 日志失败不阻断
        }
    }
}
