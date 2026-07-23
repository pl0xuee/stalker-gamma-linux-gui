using Avalonia;
using System;

namespace StalkerGamma.Gui;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Last-resort backstop: log unhandled async/background exceptions to the persistent
        // log instead of dying silently. Individual commands still handle their own errors;
        // this only catches paths that slip through.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            TryLogCrash((e.ExceptionObject as Exception)?.ToString() ?? "unknown");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TryLogCrash(e.Exception.ToString());
            e.SetObserved();
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void TryLogCrash(string detail)
    {
        try
        {
            var dir = System.IO.Path.Join(Models.CliSettings.AppDataPath, "logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Join(dir, "stalker-gamma-gui.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED: {detail}\n"
            );
        }
        catch
        {
            // nothing more we can do
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
