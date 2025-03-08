using Avalonia;
using System;
using System.Threading;

namespace WindowSwitcher;

static class Program
{
    private static Mutex mutex = new Mutex(true, "{8A6F0BA4-B5B1-45fd-A8CF-71F04B6BDE8F}");
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Only one instance of the app running !
        if (mutex.WaitOne(TimeSpan.Zero, true))
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            mutex.ReleaseMutex();            
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}