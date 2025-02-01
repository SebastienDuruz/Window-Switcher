using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows.Commands;

namespace WindowSwitcher;

public partial class FloatingWindow : Window
{
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
        if(ConfigFileAccessor.GetInstance().Config.ActivateWindowsPreview)
            while (!cancellationToken.IsCancellationRequested)
            {
                await UpdateScreenshot();
                await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
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
    }
}
