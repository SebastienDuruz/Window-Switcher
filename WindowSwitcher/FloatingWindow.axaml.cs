using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher;

public partial class FloatingWindow : Window
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    internal WindowConfig WindowConfig { get; set; }
    private WindowAccessor WindowAccessor { get; set; }
        
    public FloatingWindow(WindowConfig windowConfig, WindowAccessor windowAccessor)
    {
        InitializeComponent();
        
        WindowConfig = windowConfig;
        WindowAccessor = windowAccessor;
        
        SetInitialWindowSettings();
        Show();
        
        StartBackgroundTask();
    }
    
    private void StartBackgroundTask()
    {
        Task.Run(async () => await RunPeriodicTask(_cts.Token));
    }
    
    private async Task RunPeriodicTask(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await UpdateScreenshot();
            await Task.Delay(ConfigFileAccessor.GetInstance().Config.RefreshTimeoutMs, cancellationToken);
        }
    }

    private void SetInitialWindowSettings()
    {
        WindowConfig settingsConfig = ConfigFileAccessor.GetInstance().GetFloatingWindowConfig(WindowConfig);
        if(settingsConfig != null)
            WindowConfig = settingsConfig;
        WindowLabel.Content = WindowConfig.ShortWindowTitle;
        Width = WindowConfig.WindowWidth;
        Height = WindowConfig.WindowHeight;
        Position = new PixelPoint(WindowConfig.WindowLeft, WindowConfig.WindowTop);
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowAccessor.RaiseWindow(WindowConfig);
    }

    private Task UpdateScreenshot()
    {
        Bitmap? appScreenshot = WindowAccessor.TakeScreenshot(WindowConfig);
        if(appScreenshot is not null)
            Dispatcher.UIThread.Invoke(() =>
            {
                WindowCanvas.Background = new ImageBrush
                {
                    Source = appScreenshot,
                    Stretch = Stretch.Fill,
                };
            });
        return Task.CompletedTask;
    }
    
    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        WindowConfig.WindowHeight = Height;
        WindowConfig.WindowWidth = Width;
        
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
    }

    private void FloatingWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        WindowConfig.WindowLeft = Position.X;
        WindowConfig.WindowTop = Position.Y;
        
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
    }
}