using System;
using System.Collections.Generic;
using System.Drawing;
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

namespace WindowSwitcher;

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
        WindowConfig? settingsConfig = ConfigFileAccessor.GetInstance().GetFloatingWindowConfig(WindowConfig);
        if (settingsConfig != null)
        {
            WindowConfig = settingsConfig;

            // Position only for existing window configurations, avoid the window to pop outside the viewport on Linux
            Position = new PixelPoint(WindowConfig.WindowLeft, WindowConfig.WindowTop);
        }

        WindowLabel.Content = WindowConfig.ShortWindowTitle;
        FloatingWindowContextMenu.Items.Add(new MenuItem()
        {
            Header = "Add to blacklist",
            Command = new ContextMenuCommand(() => MainWindow.AddToBlacklist(WindowConfig.WindowTitle))
        });
        FloatingWindowContextMenu.Items.Add(new MenuItem()
        {
            Header = "Add id to temp blacklist",
            Command = new ContextMenuCommand(() => MainWindow.AddToTempBlacklist(WindowConfig.WindowId))
        });
        Width = WindowConfig.WindowWidth;
        Height = WindowConfig.WindowHeight;
        SystemDecorations = ConfigFileAccessor.GetInstance().Config.ShowWindowDecorations
            ? SystemDecorations.Full
            : SystemDecorations.BorderOnly;
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowAccessor.RaiseWindow(WindowConfig.WindowId);
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
        WindowConfig.WindowHeight = Height;
        WindowConfig.WindowWidth = Width;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RegisterWindowThumbnail();
    }

    private void WindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowConfig.WindowLeft = Position.X;
        WindowConfig.WindowTop = Position.Y;
    }

    private void FloatingWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
        e.Cancel = !StaticData.AppClosing;
        _cts.Cancel();
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ThumbnailHandle != IntPtr.Zero)
            Win32DwmFunctions.DwmUnregisterThumbnail(ThumbnailHandle);
    }

    private void RegisterWindowThumbnail()
    {
        if (ThumbnailHandle != IntPtr.Zero)
            Win32DwmFunctions.DwmUnregisterThumbnail(ThumbnailHandle);
        
        IntPtr windowHandle = this.TryGetPlatformHandle().Handle;
        IntPtr srcHandle = IntPtr.Parse(WindowConfig.WindowId);
        int res = Win32DwmFunctions.DwmRegisterThumbnail(windowHandle, srcHandle,out IntPtr thumbnail);
        if (res == 0) // all good !
        {
            ThumbnailHandle = thumbnail;
            
            Win32DwmFunctions.DwmQueryThumbnailSourceSize( thumbnail, out Win32DwmFunctions.PSIZE size );

            Win32DwmFunctions.Rect dest = new  Win32DwmFunctions.Rect()
            {
                Left = 0,
                Top = 12,
                Right = (int)WindowConfig.WindowWidth,
                Bottom = (int)WindowConfig.WindowHeight,
            };

            Win32DwmFunctions.DWM_THUMBNAIL_PROPERTIES props = new Win32DwmFunctions.DWM_THUMBNAIL_PROPERTIES();

            props.dwFlags =
                Win32DwmFunctions.DWM_TNP_SOURCECLIENTAREAONLY |
                Win32DwmFunctions.DWM_TNP_VISIBLE |
                Win32DwmFunctions.DWM_TNP_OPACITY |
                Win32DwmFunctions.DWM_TNP_RECTDESTINATION;

            props.fSourceClientAreaOnly = false;
            props.fVisible = true;
            props.opacity = 255;
            props.rcDestination = dest;

            Win32DwmFunctions.DwmUpdateThumbnailProperties(thumbnail, ref props );
        }
    }
}
