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
    private const double ResizeGripThickness = 8d;
    private static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor TopSideCursor = new(StandardCursorType.TopSide);
    private static readonly Cursor BottomSideCursor = new(StandardCursorType.BottomSide);
    private static readonly Cursor LeftSideCursor = new(StandardCursorType.LeftSide);
    private static readonly Cursor RightSideCursor = new(StandardCursorType.RightSide);
    private static readonly Cursor TopLeftCornerCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor TopRightCornerCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor BottomLeftCornerCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor BottomRightCornerCursor = new(StandardCursorType.BottomRightCorner);
    private volatile bool _isPointerInside;
    private bool _closeRequestedByHost;
    private readonly IFloatingWindowHost _floatingWindowHost;
    private readonly WinAccessorBase _winAccessorBase;
    private readonly IFloatingPreviewPolicy _floatingPreviewPolicy;
    private readonly IFloatingWindowSettingsService _floatingWindowSettingsService;
    private readonly FloatingWindowService _service;
    public WindowConfig WindowConfig { get; private set; }

    internal FloatingWindow(
        WindowConfig windowConfig,
        WinAccessorBase winAccessorBase,
        IPreviewFrameProvider previewFrameProvider,
        IFloatingWindowHost floatingWindowHost,
        IFloatingWindowSettingsService floatingWindowSettingsService
    )
    {
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(winAccessorBase);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingWindowHost);
        ArgumentNullException.ThrowIfNull(floatingWindowSettingsService);

        InitializeComponent();

        WindowConfig = windowConfig;
        _winAccessorBase = winAccessorBase;
        _floatingWindowHost = floatingWindowHost;
        _floatingWindowSettingsService = floatingWindowSettingsService;
        _floatingPreviewPolicy = AppServiceProvider.GetRequiredService<IFloatingPreviewPolicy>();
        var nativeThumbnailRenderer = AppServiceProvider.GetRequiredService<INativeThumbnailRenderer>();
        var floatingWindowHandleConfigurator = AppServiceProvider.GetRequiredService<IFloatingWindowHandleConfigurator>();

        SetInitialWindowSettings();

        _service = new FloatingWindowService(
            this,
            WindowConfig,
            previewFrameProvider,
            _floatingPreviewPolicy,
            nativeThumbnailRenderer,
            WindowScreenshot,
            PreviewBorder
        );

        Show();
        _service.UpdateLayout();
        _service.Start();

        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is not null)
            floatingWindowHandleConfigurator.Configure(platformHandle.Handle);
    }

    public sealed override void Show()
    {
        base.Show();
    }

    private void SetInitialWindowSettings()
    {
        WindowConfig? settingsConfig = _floatingWindowSettingsService.GetPersistedConfig(
            WindowConfig
        );
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

        bool moveWindows = _floatingWindowSettingsService.GetBehaviorSettings().MoveWindows;

        if (TryBeginResizeDrag(e))
            return;

        if (moveWindows)
            BeginMoveDrag(e);
    }

    private bool TryBeginResizeDrag(PointerPressedEventArgs e)
    {
        if (!CanResize)
            return false;

        PointerPoint pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
            return false;

        WindowEdge? resizeEdge = ResolveResizeEdge(pointerPoint.Position);
        if (!resizeEdge.HasValue)
            return false;

        BeginResizeDrag(resizeEdge.Value, e);
        return true;
    }

    private WindowEdge? ResolveResizeEdge(Point pointerPosition)
    {
        if (Width <= 0 || Height <= 0)
            return null;

        bool isNearLeft = pointerPosition.X <= ResizeGripThickness;
        bool isNearRight = pointerPosition.X >= Width - ResizeGripThickness;
        bool isNearTop = pointerPosition.Y <= ResizeGripThickness;
        bool isNearBottom = pointerPosition.Y >= Height - ResizeGripThickness;

        if (isNearTop && isNearLeft)
            return WindowEdge.NorthWest;
        if (isNearTop && isNearRight)
            return WindowEdge.NorthEast;
        if (isNearBottom && isNearLeft)
            return WindowEdge.SouthWest;
        if (isNearBottom && isNearRight)
            return WindowEdge.SouthEast;
        if (isNearTop)
            return WindowEdge.North;
        if (isNearBottom)
            return WindowEdge.South;
        if (isNearLeft)
            return WindowEdge.West;
        if (isNearRight)
            return WindowEdge.East;

        return null;
    }

    private void UpdateResizeCursor(PointerEventArgs e)
    {
        if (!CanResize)
        {
            WindowCanvas.Cursor = DefaultCursor;
            return;
        }

        PointerPoint pointerPoint = e.GetCurrentPoint(this);
        WindowEdge? edge = ResolveResizeEdge(pointerPoint.Position);
        WindowCanvas.Cursor = ResolveCursor(edge);
    }

    private static Cursor ResolveCursor(WindowEdge? edge)
    {
        return edge switch
        {
            WindowEdge.North => TopSideCursor,
            WindowEdge.South => BottomSideCursor,
            WindowEdge.West => LeftSideCursor,
            WindowEdge.East => RightSideCursor,
            WindowEdge.NorthWest => TopLeftCornerCursor,
            WindowEdge.NorthEast => TopRightCornerCursor,
            WindowEdge.SouthWest => BottomLeftCornerCursor,
            WindowEdge.SouthEast => BottomRightCornerCursor,
            _ => DefaultCursor,
        };
    }

    private void CanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = RaiseWindowAndNotifyActivationAsync();
    }

    private void CanvasPointerEntered(object? sender, PointerEventArgs e)
    {
        UpdateResizeCursor(e);

        if (_isPointerInside)
            return;
        _isPointerInside = true;

        if (!_floatingWindowSettingsService.GetBehaviorSettings().FocusOnHover)
            return;

        PointerPoint point = e.GetCurrentPoint(this);
        if (
            point.Properties.IsLeftButtonPressed
            || point.Properties.IsRightButtonPressed
            || point.Properties.IsMiddleButtonPressed
        )
            return;

        _floatingWindowHost.SetActivePreview(this);
        _ = RaiseWindowAndNotifyActivationAsync();
    }

    private async Task RaiseWindowAndNotifyActivationAsync()
    {
        try
        {
            await _winAccessorBase.RaiseWindowAsync(WindowConfig.WindowId);
            _floatingWindowHost.NotifyPreviewWindowActivated(WindowConfig.WindowId);
        }
        catch (Exception)
        {
            // Best-effort activation path.
        }
    }

    private void CanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInside = false;
        WindowCanvas.Cursor = DefaultCursor;
    }

    private void CanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateResizeCursor(e);
    }

    private void FloatingWindowResized(object? sender, WindowResizedEventArgs e)
    {
        WindowConfig.WindowHeight = (Int32)Height;
        WindowConfig.WindowWidth = (Int32)Width;
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
        _floatingWindowSettingsService.Save(WindowConfig);
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
        FloatingWindowAppearanceSettings configSnapshot =
            _floatingWindowSettingsService.GetAppearanceSettings();

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

        if (!CanResize)
            WindowCanvas.Cursor = DefaultCursor;

        SystemDecorations = SystemDecorations.BorderOnly;

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
