using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.Interop;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Windows;

public partial class FloatingWindow : Window
{
    private IntPtr ThumbnailHandle { get; set; } = IntPtr.Zero;
    
    private readonly CancellationTokenSource _cts = new();
    public WindowConfig? WindowConfig { get; set; }
    private MainWindow MainWindow { get; set; }
    private WindowAccessor WindowAccessor { get; set; }
    
    public FloatingWindow(WindowConfig? windowConfig, WindowAccessor windowAccessor, MainWindow mainWindow)
    {
        InitializeComponent();

        WindowConfig = windowConfig;
        WindowAccessor = windowAccessor;
        MainWindow = mainWindow;

        SetInitialWindowSettings();
        Show();

        StartBackgroundTask();
    }

    public sealed override void Show()
    {
        base.Show();
    }

    private void StartBackgroundTask()
    {
        Task.Run(async () => await RunPeriodicTask(_cts.Token));
    }

    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        if (ConfigFileAccessor.GetInstance().Config.ActivateWindowsPreview)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) // Windows DWM Thumbnails
            {
                RegisterWindowThumbnail();
            }
            else // Screenshot
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateScreenshot();
                    await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
                }
            }
        }
    }

    private void SetInitialWindowSettings()
    {
        WindowConfig? settingsConfig = ConfigFileAccessor.GetInstance().GetFloatingWindowConfig(WindowConfig!);
        if (settingsConfig != null)
        {
            WindowConfig = settingsConfig;

            // Position only for existing window configurations, avoid the window to pop outside the viewport on Linux
            Position = new PixelPoint(WindowConfig.WindowLeft, WindowConfig.WindowTop);
        }
        
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            User32Functions.HideFromAltTab(TryGetPlatformHandle()!.Handle);

        WindowLabel.Content = WindowConfig!.ShortWindowTitle;
        FloatingWindowContextMenu.Items.Add(new MenuItem()
        {
            Header = "Add to blacklist",
            Command = new ContextMenuCommand(() => MainWindow.AddToBlacklist(WindowConfig.WindowTitle))
        });
        FloatingWindowContextMenu.Items.Add(new MenuItem()
        {
            Header = "Add to temp blacklist",
            Command = new ContextMenuCommand(() => MainWindow.AddToTempBlacklist(WindowConfig.WindowId))
        });
        FloatingWindowContextMenu.Items.Add(new MenuItem()
        {
            Header = "Rename window",
            Command = new ContextMenuCommand(() => _ = RenameWindowTitle())
        });
        
        CanResize = ConfigFileAccessor.GetInstance().Config.ResizeWindows;
        if (ConfigFileAccessor.GetInstance().Config.UseFixedWindowSize)
        {
            CanResize = false;
            Width = ConfigFileAccessor.GetInstance().Config.WindowWidth;
            Height = ConfigFileAccessor.GetInstance().Config.WindowHeight; 
        }
        else
        {
            Width = WindowConfig.WindowWidth;
            Height = WindowConfig.WindowHeight;
        }
        
        SystemDecorations = ConfigFileAccessor.GetInstance().Config.ShowWindowDecorations
            ? SystemDecorations.Full
            : SystemDecorations.BorderOnly;
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if(ConfigFileAccessor.GetInstance().Config.MoveWindows)
            BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowAccessor.RaiseWindow(WindowConfig!.WindowId);
    }

    private Task UpdateScreenshot()
    {
        Bitmap appScreenshot = WindowAccessor.TakeScreenshot(WindowConfig.WindowId);
        if (appScreenshot is not null)
            Dispatcher.UIThread.Invoke(() =>
            {
                WindowCanvas.Background = new ImageBrush()
                {
                    Source = appScreenshot,
                    Stretch = Stretch.Fill,
                    Opacity = 0.8
                };
            });
        return Task.CompletedTask;
    }

    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        WindowConfig!.WindowHeight = Height;
        WindowConfig.WindowWidth = Width;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ConfigFileAccessor.GetInstance().Config.ActivateWindowsPreview)
            RegisterWindowThumbnail();    
    }

    private void WindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowConfig!.WindowLeft = Position.X;
        WindowConfig.WindowTop = Position.Y;
    }

    private void FloatingWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
        e.Cancel = !StaticData.AppClosing;
        if (!e.Cancel)
        {
            _cts.Cancel();
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ThumbnailHandle != IntPtr.Zero)
                DwmFunctions.DwmUnregisterThumbnail(ThumbnailHandle);    
        }
    }

    private void RegisterWindowThumbnail()
    {
        if (ThumbnailHandle != IntPtr.Zero)
            DwmFunctions.DwmUnregisterThumbnail(ThumbnailHandle);
        
        IntPtr windowHandle = TryGetPlatformHandle()!.Handle;
        IntPtr srcHandle = IntPtr.Parse(WindowConfig!.WindowId);
        int res = DwmFunctions.DwmRegisterThumbnail(windowHandle, srcHandle,out IntPtr thumbnail);
        if (res == 0) // all good !
        {
            ThumbnailHandle = thumbnail;
            
            DwmFunctions.DwmQueryThumbnailSourceSize( thumbnail, out DwmFunctions.PSIZE size );
            DwmFunctions.Rect dest = new()
            {
                Left = 0,
                Top = (int)(12 * Screens.Primary!.Scaling),
                Right = (int)(WindowConfig.WindowWidth * Screens.Primary.Scaling),
                Bottom = (int)(WindowConfig.WindowHeight * Screens.Primary.Scaling),
            };

            DwmFunctions.DWM_THUMBNAIL_PROPERTIES props = new DwmFunctions.DWM_THUMBNAIL_PROPERTIES();

            props.dwFlags =
                DwmFunctions.DWM_TNP_SOURCECLIENTAREAONLY |
                DwmFunctions.DWM_TNP_VISIBLE |
                DwmFunctions.DWM_TNP_OPACITY |
                DwmFunctions.DWM_TNP_RECTDESTINATION;

            props.fSourceClientAreaOnly = false;
            props.fVisible = true;
            props.opacity = 255;
            props.rcDestination = dest;

            DwmFunctions.DwmUpdateThumbnailProperties(thumbnail, ref props );
        }
    }

    private async Task RenameWindowTitle()
    {
        // Works only for windows
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await MainWindow.RenameWindowTitle(WindowConfig!.WindowId);
    }
}
