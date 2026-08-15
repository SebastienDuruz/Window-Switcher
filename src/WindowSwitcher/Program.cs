using System;
using System.Threading;
using Avalonia;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data.Platform.Graphics;

namespace WindowSwitcher;

static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppServiceProvider.Initialize();
        using var mutex = new Mutex(false, "{8A6F0BA4-B5B1-45fd-A8CF-71F04B6BDE8F}");

        // Only one instance of the app running !
        if (mutex.WaitOne(TimeSpan.Zero, true))
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            mutex.ReleaseMutex();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UseSkia().UsePlatformDetect();

        if (OperatingSystem.IsLinux())
        {
            builder = builder.With(
                new X11PlatformOptions { RenderingMode = CreateLinuxRenderingModes() }
            );
        }
        else if (OperatingSystem.IsWindows())
        {
            builder = builder.With(
                new Win32PlatformOptions { RenderingMode = CreateWindowsRenderingModes() }
            );
        }

        return builder.WithInterFont();
    }

    private static X11RenderingMode[] CreateLinuxRenderingModes()
    {
        return LinuxEglPlatformConfigurator.IsX11Configured
            ?
            [
                X11RenderingMode.Egl,
                X11RenderingMode.Vulkan,
                X11RenderingMode.Glx,
                X11RenderingMode.Software,
            ]
            :
            [
                X11RenderingMode.Vulkan,
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software,
            ];
    }

    private static Win32RenderingMode[] CreateWindowsRenderingModes()
    {
        return
        [
            Win32RenderingMode.Vulkan,
            Win32RenderingMode.AngleEgl,
            Win32RenderingMode.Software,
        ];
    }
}
