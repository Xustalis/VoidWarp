using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoidWarp.Windows;

/// <summary>
/// Application entry point. Registers global exception handlers for reliability.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to prevent unhandled crashes
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Show splash first, then main window
        var splash = new SplashWindow();
        splash.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var main = new MainWindow();
            main.Show();
            splash.Close();
        };
        timer.Start();
    }

    /// <summary>
    /// Non-UI thread or app-domain level unhandled exception (e.g. background thread crash).
    /// </summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = ex?.Message ?? e.ExceptionObject?.ToString() ?? "Unknown error";
        try
        {
            LogException("UnhandledException", ex);
            MessageBox.Show(
                $"发生未处理的错误，程序可能即将退出。\n\n{message}\n\n请尝试重新启动应用。",
                "VoidWarp - 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Last resort: avoid secondary crash
        }
    }

    /// <summary>
    /// UI thread unhandled exception. Setting e.Handled = true keeps the app running.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            LogException("DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                $"操作时发生错误：\n\n{e.Exception.Message}\n\n程序将继续运行。若问题重复出现，请重启应用。",
                "VoidWarp - 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Avoid secondary crash
        }

        e.Handled = true; // Prevent app crash
    }

    /// <summary>
    /// Unobserved task exception (e.g. async void or forgotten await). Mark as observed to avoid process crash.
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            LogException("UnobservedTaskException", e.Exception?.Flatten().InnerException ?? e.Exception);
        }
        catch
        {
            // Ignore
        }

        e.SetObserved();
    }

    /// <summary>
    /// Write exception to a log file in app data for diagnostics.
    /// </summary>
    private static void LogException(string source, Exception? ex)
    {
        if (ex == null) return;

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidWarp");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "VoidWarp_errors.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(path, line);
        }
        catch
        {
            // Best-effort only
        }
    }
}
