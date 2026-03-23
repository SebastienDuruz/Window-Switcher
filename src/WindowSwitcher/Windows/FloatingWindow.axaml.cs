using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WindowSwitcher.Controls;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.Windows.Abstractions;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class FloatingWindow : Window, IFloatingPreviewWindow
{
    private volatile bool _isPointerInside;
    private bool _closeRequestedByHost;
    private readonly IFloatingWindowHost _floatingWindowHost;
    private readonly WinAccessorBase _winAccessorBase;
    private readonly IFloatingPreviewPolicy _floatingPreviewPolicy;
    private readonly IFloatingWindowHandleConfigurator _floatingWindowHandleConfigurator;
    private readonly FloatingWindowService _service;
    public WindowConfig WindowConfig { get; private set; }

    public FloatingWindow(
        WindowConfig windowConfig,
        WinAccessorBase winAccessorBase,
        IPreviewFrameProvider previewFrameProvider,
        IFloatingWindowHost floatingWindowHost
    )
    {
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(winAccessorBase);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingWindowHost);

        InitializeComponent();

        WindowConfig = windowConfig;
        _winAccessorBase = winAccessorBase;
        _floatingWindowHost = floatingWindowHost;
        _floatingPreviewPolicy = AppServiceProvider.GetRequiredService<IFloatingPreviewPolicy>();
        _floatingWindowHandleConfigurator =
            AppServiceProvider.GetRequiredService<IFloatingWindowHandleConfigurator>();

        SetInitialWindowSettings();

        _service = new FloatingWindowService(
            this,
            WindowConfig,
            previewFrameProvider,
            _floatingPreviewPolicy,
            WindowScreenshot,
            PreviewBorder
        );

        Show();
        _service.UpdateLayout();
        _service.Start();

        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is not null)
            _floatingWindowHandleConfigurator.Configure(platformHandle.Handle);
    }

    public sealed override void Show()
    {
        base.Show();
    }

    private void SetInitialWindowSettings()
    {
        WindowConfig? settingsConfig = ConfigFileAccessor
            .GetInstance()
            .GetFloatingWindowConfig(WindowConfig);
        if (settingsConfig != null)
        {
            WindowConfig = settingsConfig;

            // Position only for existing window configurations, avoid the window to pop outside the viewport on Linux
            Position = new PixelPoint(WindowConfig.WindowLeft, WindowConfig.WindowTop);
        }

        WindowLabel.Content = WindowConfig.ShortWindowTitle;

        WindowScreenshot.IsVisible = _floatingPreviewPolicy.ShowScreenshotControl;
        FloatingWindowContextMenu.Items.Add(
            new MenuItem()
            {
                Header = "Add to blacklist",
                Command = new ContextMenuCommand(() =>
                    _floatingWindowHost.AddToBlacklist(WindowConfig.WindowTitle)
                ),
            }
        );
        FloatingWindowContextMenu.Items.Add(
            new MenuItem()
            {
                Header = "Add to temp blacklist",
                Command = new ContextMenuCommand(() =>
                    _floatingWindowHost.AddToTempBlacklist(WindowConfig.WindowId)
                ),
            }
        );
        FloatingWindowContextMenu.Items.Add(
            new MenuItem()
            {
                Header = "Rename window",
                Command = new ContextMenuCommand(() => _ = RenameWindowTitleAsync()),
            }
        );

        ApplySettingsCore(refreshPreviewPipeline: false);
    }

    private void CanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _floatingWindowHost.SetActivePreview(this);
        if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.MoveWindows))
            BeginMoveDrag(e);
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _winAccessorBase.RaiseWindow(WindowConfig.WindowId);
        _floatingWindowHost.NotifyPreviewWindowActivated(WindowConfig.WindowId);
    }

    private void CanvasPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_isPointerInside)
            return;
        _isPointerInside = true;

        if (!ConfigFileAccessor.GetInstance().ReadConfig(config => config.FocusOnHover))
            return;

        PointerPoint point = e.GetCurrentPoint(this);
        if (
            point.Properties.IsLeftButtonPressed
            || point.Properties.IsRightButtonPressed
            || point.Properties.IsMiddleButtonPressed
        )
            return;

        _floatingWindowHost.SetActivePreview(this);
        _winAccessorBase.RaiseWindow(WindowConfig.WindowId);
        _floatingWindowHost.NotifyPreviewWindowActivated(WindowConfig.WindowId);
    }

    private void CanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInside = false;
    }

    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        WindowConfig.WindowHeight = Height;
        WindowConfig.WindowWidth = Width;
        _service.OnWindowResized();
    }

    private void WindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        WindowConfig.WindowLeft = Position.X;
        WindowConfig.WindowTop = Position.Y;
        _service.OnPointerReleased();
    }

    private void FloatingWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(WindowConfig);
        _floatingWindowHost.ClearActivePreview(this);
        bool allowClose = StaticData.AppClosing || _closeRequestedByHost;
        e.Cancel = !allowClose;
        if (!e.Cancel)
            _service.Stop(WindowConfig.WindowId);
    }

    private async Task RenameWindowTitleAsync()
    {
        await _floatingWindowHost.RenameWindowTitleAsync(WindowConfig.WindowId);
    }

    public void SetPreviewHighlight(bool isSelected)
    {
        _service.SetPreviewHighlight(isSelected);
    }

    public void UpdateWindowTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            return;

        WindowConfig.WindowTitle = newTitle;
        WindowLabel.Content = WindowConfig.ShortWindowTitle;
    }

    public void ApplySettings()
    {
        ApplySettingsCore(refreshPreviewPipeline: true);
    }

    public void RequestCloseFromHost()
    {
        if (_closeRequestedByHost)
            return;

        _closeRequestedByHost = true;
        _service.Stop(WindowConfig.WindowId);
        Close();
    }

    private void ApplySettingsCore(bool refreshPreviewPipeline)
    {
        var configSnapshot = ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => new
            {
                config.ResizeWindows,
                config.UseFixedWindowSize,
                config.WindowWidth,
                config.WindowHeight,
                config.ShowWindowDecorations,
                config.PreviewHighlightColor,
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

        if (refreshPreviewPipeline)
            _service.ApplySettings();
    }
}
