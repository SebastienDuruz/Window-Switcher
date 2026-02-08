using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.CustomWindows.Commands;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.Interop;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Windows;

public partial class FloatingWindow : Window
{
    private const double TitleReservedHeight = 12;
    private const double PreviewBorderThickness = 2;
    private IntPtr ThumbnailHandle { get; set; } = IntPtr.Zero;
    private bool _isPointerInside;
    
    private readonly CancellationTokenSource _cts = new();
    private Bitmap? _currentScreenshot;
    private static readonly SemaphoreSlim ScreenshotSemaphore = new(1, 1);
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
        var configAccessor = ConfigFileAccessor.GetInstance();
        if (configAccessor.ReadConfig(config => config.ActivateWindowsPreview))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) // Windows DWM Thumbnails
            {
                RegisterWindowThumbnail();
            }
            else // Screenshot
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateScreenshot(cancellationToken);
                    int refreshTimeoutMs = configAccessor.ReadConfig(config => config.ScreenshotRefreshTimeoutMs);
                    await Task.Delay(refreshTimeoutMs, cancellationToken);
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
        WindowScreenshot.IsVisible = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
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
        
        var configSnapshot = ConfigFileAccessor.GetInstance().ReadConfig(config => new
        {
            config.ResizeWindows,
            config.UseFixedWindowSize,
            config.WindowWidth,
            config.WindowHeight,
            config.ShowWindowDecorations,
            config.PreviewHighlightColor
        });

        CanResize = configSnapshot.ResizeWindows;
        if (configSnapshot.UseFixedWindowSize)
        {
            CanResize = false;
            Width = configSnapshot.WindowWidth;
            Height = configSnapshot.WindowHeight; 
        }
        else
        {
            Width = WindowConfig.WindowWidth;
            Height = WindowConfig.WindowHeight;
        }
        
        SystemDecorations = configSnapshot.ShowWindowDecorations
            ? SystemDecorations.Full
            : SystemDecorations.BorderOnly;

        if (Color.TryParse(configSnapshot.PreviewHighlightColor, out Color highlightColor))
        {
            var highlightBrush = new SolidColorBrush(highlightColor);
            WindowLabel.Foreground = highlightBrush;
            PreviewBorder.BorderBrush = highlightBrush;
        }
        else
        {
            PreviewBorder.BorderBrush = WindowLabel.Foreground;
        }

        UpdatePreviewLayout();
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MainWindow.SetActivePreview(this);
        if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.MoveWindows))
            BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowAccessor.RaiseWindow(WindowConfig!.WindowId);
    }

    private void CanvasPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_isPointerInside)
            return;
        _isPointerInside = true;

        if (!ConfigFileAccessor.GetInstance().ReadConfig(config => config.FocusOnHover))
            return;

        if (WindowConfig is null)
            return;

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
            return;

        MainWindow.SetActivePreview(this);
        WindowAccessor.RaiseWindow(WindowConfig.WindowId);
    }

    private void CanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInside = false;
    }

    private async Task UpdateScreenshot(CancellationToken cancellationToken)
    {
        if (WindowConfig is null)
            return;

        Bitmap? appScreenshot = null;
        bool lockTaken = false;
        try
        {
            await ScreenshotSemaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            appScreenshot = WindowAccessor.TakeScreenshot(WindowConfig.WindowId);
        }
        finally
        {
            if (lockTaken)
                ScreenshotSemaphore.Release();
        }

        if (appScreenshot is null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bitmap? previous = _currentScreenshot;
            _currentScreenshot = appScreenshot;
            WindowScreenshot.Source = appScreenshot;
            previous?.Dispose();
        });
    }

    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        WindowConfig!.WindowHeight = Height;
        WindowConfig.WindowWidth = Width;
        UpdatePreviewLayout();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateWindowsPreview))
            RegisterWindowThumbnail();    
    }

    private void WindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowConfig!.WindowLeft = Position.X;
        WindowConfig.WindowTop = Position.Y;
    }

    private void FloatingWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (WindowConfig is not null)
            ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
        MainWindow.ClearActivePreview(this);
        e.Cancel = !StaticData.AppClosing;
        if (!e.Cancel)
        {
            _cts.Cancel();
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ThumbnailHandle != IntPtr.Zero)
                DwmFunctions.DwmUnregisterThumbnail(ThumbnailHandle);
            _currentScreenshot?.Dispose();
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
            double scale = Screens.Primary!.Scaling;
            int inset = (int)Math.Round(PreviewBorderThickness * scale);
            DwmFunctions.Rect dest = new()
            {
                Left = inset,
                Top = (int)(TitleReservedHeight * scale) + inset,
                Right = (int)(WindowConfig.WindowWidth * scale) - inset,
                Bottom = (int)(WindowConfig.WindowHeight * scale) - inset,
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
        await MainWindow.RenameWindowTitle(WindowConfig!.WindowId);
    }

    public void SetPreviewHighlight(bool isSelected)
    {
        PreviewBorder.IsVisible = isSelected;
    }

    private void UpdatePreviewLayout()
    {
        double topOffset = TitleReservedHeight;
        double previewWidth = Math.Max(0, Width);
        double previewHeight = Math.Max(0, Height - topOffset);
        double left = 0;
        double top = topOffset;

        left = RoundToPixel(left);
        top = RoundToPixel(top);
        previewWidth = RoundToPixel(previewWidth);
        previewHeight = RoundToPixel(previewHeight);

        Canvas.SetLeft(PreviewBorder, left);
        Canvas.SetTop(PreviewBorder, top);
        PreviewBorder.Width = previewWidth;
        PreviewBorder.Height = previewHeight;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Canvas.SetLeft(WindowScreenshot, left);
        Canvas.SetTop(WindowScreenshot, top);
        WindowScreenshot.Width = previewWidth;
        WindowScreenshot.Height = previewHeight;
    }

    private double RoundToPixel(double value)
    {
        double scale = RenderScaling;
        if (scale <= 0)
            scale = 1;
        return Math.Round(value * scale) / scale;
    }
}
