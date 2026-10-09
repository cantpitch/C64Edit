using Avalonia;
using Avalonia.Threading;

namespace C64Edit;

internal static class Program
{
    public static string CrashLogPath => Path.Combine(Settings.Directory, "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    /// <summary>Writes an unhandled exception to stderr and crash.log next to settings.json.</summary>
    public static void LogCrash(Exception? ex)
    {
        var text = $"[{DateTime.Now:O}] {ex}\n";
        Console.Error.WriteLine(text);
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Directory);
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
            // Nothing more we can do.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .AfterSetup(_ => Dispatcher.UIThread.UnhandledException += (_, e) => LogCrash(e.Exception));
}
