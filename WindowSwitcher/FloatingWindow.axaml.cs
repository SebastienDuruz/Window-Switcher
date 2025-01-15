using Avalonia.Controls;
using Avalonia.Input;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcher;

public partial class FloatingWindow : Window
{
    internal WindowConfig WindowConfig { get; set; }
    private WindowAccessor WindowAccessor { get; set; }
        
    public FloatingWindow(WindowConfig windowConfig, WindowAccessor windowAccessor)
    {
        InitializeComponent();
        
        this.WindowConfig = windowConfig;
        this.WindowAccessor = windowAccessor;
        
        this.SetInitialWindowSettings();
        this.Show();
    }

    private void SetInitialWindowSettings()
    {
        WindowConfig settingsConfig = ConfigFileAccessor.GetInstance().GetFloatingWindowConfig(this.WindowConfig);
        if(settingsConfig != null)
            this.WindowConfig = settingsConfig;
        this.WindowLabel.Content = WindowConfig.ShortWindowTitle;
        this.Width = WindowConfig.WindowWidth;
        this.Height = WindowConfig.WindowHeight;
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // if(e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        // else if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        this.BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        this.WindowAccessor.RaiseWindow(this.WindowConfig);
    }

    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        this.WindowConfig.WindowHeight = this.Height;
        this.WindowConfig.WindowWidth = this.Width;
        this.WindowConfig.WindowLeft = this.Position.X;
        this.WindowConfig.WindowTop = this.Position.Y;
        
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(this.WindowConfig);
    }
}