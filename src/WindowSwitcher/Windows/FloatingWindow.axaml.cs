using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Serilog;
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
    private const double DefaultSlotMargin = 16d;
    private const double DefaultSlotGap = 8d;
    private static readonly ILogger Logger = Log.ForContext<FloatingWindow>();
    private static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor TopSideCursor = new(StandardCursorType.TopSide);
    private static readonly Cursor BottomSideCursor = new(StandardCursorType.BottomSide);
    private static readonly Cursor LeftSideCursor = new(StandardCursorType.LeftSide);
    private static readonly Cursor RightSideCursor = new(StandardCursorType.RightSide);
    private static readonly Cursor TopLeftCornerCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor TopRightCornerCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor BottomLeftCornerCursor = new(
        StandardCursorType.BottomLeftCorner
    );
    private static readonly Cursor BottomRightCornerCursor = new(
        StandardCursorType.BottomRightCorner
    );
    private const string PendingSelectionBorderBrushKey = "PendingSelectionBorderBrush";
    private static readonly IBrush DefaultPendingSelectionBorderBrush = new SolidColorBrush(
        Color.FromRgb(255, 185, 0)
    );
    private readonly IBrush _pendingSelectionBorderBrush;
    private volatile bool _isPointerInside;
    private bool _closeRequestedByHost;
    private bool _isPreviewSelected;
    private bool _isPreviewHidden;
    private bool _showFloatingWindow = true;
    private bool _isConstructed;
    private PixelPoint _positionBeforeHide;
    private bool _isSelectionPending;
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
        IFloatingWindowSettingsService floatingWindowSettingsService,
        Func<string, int> acquireDefaultSlot
    )
    {
        ArgumentNullException.ThrowIfNull(windowConfig);
        ArgumentNullException.ThrowIfNull(winAccessorBase);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingWindowHost);
        ArgumentNullException.ThrowIfNull(floatingWindowSettingsService);
        ArgumentNullException.ThrowIfNull(acquireDefaultSlot);

        InitializeComponent();

        _pendingSelectionBorderBrush = ResolvePendingSelectionBorderBrush();

        WindowConfig = windowConfig;
        _winAccessorBase = winAccessorBase;
        _floatingWindowHost = floatingWindowHost;
        _floatingWindowSettingsService = floatingWindowSettingsService;
        _floatingPreviewPolicy = AppServiceProvider.GetRequiredService<IFloatingPreviewPolicy>();
        var nativeThumbnailRenderer =
            AppServiceProvider.GetRequiredService<INativeThumbnailRenderer>();
        var floatingWindowHandleConfigurator =
            AppServiceProvider.GetRequiredService<IFloatingWindowHandleConfigurator>();

        SetInitialWindowSettings(acquireDefaultSlot);

        _service = new FloatingWindowService(
            this,
            WindowConfig,
            previewFrameProvider,
            _floatingPreviewPolicy,
            nativeThumbnailRenderer,
            WindowScreenshot,
            DmaBufPreview,
            PreviewBorder
        );

        // The native handle exists once the window is shown, which can happen later when
        // floating previews are hidden by the settings.
        Opened += (_, _) =>
        {
            var platformHandle = TryGetPlatformHandle();
            if (platformHandle is not null)
                floatingWindowHandleConfigurator.Configure(platformHandle.Handle);
        };

        _isConstructed = true;
        if (IsHiddenForNow)
            _positionBeforeHide = Position;
        else
            Show();
        _service.UpdateLayout();
        _service.Start();
    }

    // Hidden by "Hide active preview" or by the "Show floating previews" setting.
    private bool IsHiddenForNow => _isPreviewHidden || !_showFloatingWindow;

    public sealed override void Show()
    {
        base.Show();
    }

    private void SetInitialWindowSettings(Func<string, int> acquireDefaultSlot)
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
        else
        {
            // No saved position: place the preview in its own slot so new previews do not
            // open on top of each other.
            PlaceInDefaultSlot(acquireDefaultSlot(WindowConfig.WindowId));
        }

        WindowLabel.Text = WindowConfig.ShortWindowTitle;

        // Avalonia draws its own shadow and frame around BorderOnly windows on X11/XWayland,
        // which shows up as a black margin around the preview; resizing is handled by the canvas.
        if (!_floatingPreviewPolicy.UseWindowBorder)
            WindowDecorations = WindowDecorations.None;

        WindowScreenshot.IsVisible = _floatingPreviewPolicy.ShowScreenshotControl;
        DmaBufPreview.IsVisible = _floatingPreviewPolicy.ShowScreenshotControl;
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
        if (_floatingWindowHost.CanResetPreviewSelection)
        {
            FloatingWindowContextMenu.Items.Add(new Separator());
            FloatingWindowContextMenu.Items.Add(
                new MenuItem()
                {
                    Header = "Reset preview stream",
                    Command = new ContextMenuCommand(() =>
                        _floatingWindowHost.ResetPreviewSelection(WindowConfig.WindowId)
                    ),
                }
            );
        }

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

    /// <summary>
    /// Activates the window of this preview as if the preview had been clicked: the
    /// preview becomes the active one, then its window is brought to the foreground.
    /// </summary>
    internal Task ActivateFromHostAsync()
    {
        _floatingWindowHost.SetActivePreview(this);
        return RaiseWindowAndNotifyActivationAsync();
    }

    private async Task RaiseWindowAndNotifyActivationAsync()
    {
        try
        {
            bool activated = await _winAccessorBase.TryActivateWindowAsync(WindowConfig.WindowId);
            if (!activated)
                return;
            _floatingWindowHost.NotifyPreviewWindowActivated(WindowConfig.WindowId);
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "Activating window {WindowId} from its preview failed",
                WindowConfig.WindowId
            );
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
        _isPreviewSelected = isSelected;
        if (!_isSelectionPending)
            _service.SetPreviewHighlight(isSelected);
    }

    public void SetPreviewHidden(bool isHidden)
    {
        if (isHidden == _isPreviewHidden)
            return;

        _isPreviewHidden = isHidden;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (!_isConstructed)
            return;

        bool visible = !IsHiddenForNow;
        if (visible == IsVisible)
            return;

        if (!visible)
        {
            // WindowConfig only tracks the position after a pointer release, so a preview that was
            // never moved would come back at (0, 0); keep the actual position instead.
            _positionBeforeHide = Position;
            Hide();
            return;
        }

        // Some Linux window managers do not restore the position of a window shown again.
        Position = _positionBeforeHide;
        Show();

        // X11 window managers drop _NET_WM_STATE (including "above") when a window is withdrawn,
        // and Avalonia does not send it again on map because Topmost did not change; toggle it so
        // the preview stays above other windows after being shown again.
        Topmost = false;
        Topmost = true;
    }

    internal void SetSelectionPending(bool isPending)
    {
        _isSelectionPending = isPending;
        ApplyPreviewBorderAppearance();
        _service.SetPreviewHighlight(isPending || _isPreviewSelected);
    }

    public void UpdateWindowTitle(string newTitle, string originalTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            return;

        WindowConfig.WindowTitle = newTitle;
        WindowConfig.OriginalWindowTitle = originalTitle;
        WindowLabel.Text = WindowConfig.ShortWindowTitle;
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

    private void PlaceInDefaultSlot(int slot)
    {
        Screen? screen = Screens?.Primary;
        if (screen is null)
            return;

        FloatingWindowAppearanceSettings appearance =
            _floatingWindowSettingsService.GetAppearanceSettings();
        double width = appearance.UseFixedWindowSize
            ? appearance.WindowWidth
            : WindowConfig.WindowWidth;
        double height = appearance.UseFixedWindowSize
            ? appearance.WindowHeight
            : WindowConfig.WindowHeight;
        double scaling = screen.Scaling;

        PixelPoint position = PreviewSlotLayout.GetPosition(
            slot,
            screen.WorkingArea,
            new PixelSize((int)Math.Ceiling(width * scaling), (int)Math.Ceiling(height * scaling)),
            margin: (int)Math.Round(DefaultSlotMargin * scaling),
            gap: (int)Math.Round(DefaultSlotGap * scaling)
        );

        // A hidden preview takes its position back when it is shown again.
        if (IsHiddenForNow)
            _positionBeforeHide = position;
        else
            Position = position;

        // WindowConfig otherwise only follows the position after a pointer release, and it is
        // what gets saved when the preview closes.
        WindowConfig.WindowLeft = position.X;
        WindowConfig.WindowTop = position.Y;
    }

    /// <summary>
    /// Moves this preview next to <paramref name="anchor"/> (bounds in physical pixels)
    /// and raises it above the other windows, so a preview lost off screen or behind
    /// other windows can be found and placed again.
    /// </summary>
    internal void MoveNextTo(PixelRect anchor)
    {
        Screen? screen = Screens?.ScreenFromPoint(anchor.Center) ?? Screens?.Primary;
        if (screen is null)
            return;

        double scaling = screen.Scaling;
        PixelPoint position = PreviewPlacement.NextTo(
            anchor,
            PixelSize.FromSize(new Size(Width, Height), scaling),
            screen.WorkingArea,
            gap: (int)Math.Round(DefaultSlotGap * scaling)
        );

        if (IsHiddenForNow)
        {
            _positionBeforeHide = position;
        }
        else
        {
            Position = position;
            // Re-assert "above" so the preview comes back over windows that cover it.
            Topmost = false;
            Topmost = true;
        }

        WindowConfig.WindowLeft = position.X;
        WindowConfig.WindowTop = position.Y;
    }

    /// <summary>
    /// Forgets the size and position of this preview, then places it in
    /// <paramref name="slot"/>, as for a preview opened without a saved layout.
    /// </summary>
    internal void ResetLayout(int slot)
    {
        var defaults = new WindowConfig();
        WindowConfig.WindowWidth = defaults.WindowWidth;
        WindowConfig.WindowHeight = defaults.WindowHeight;
        ApplySettingsCore(refreshPreviewPipeline: false);
        PlaceInDefaultSlot(slot);
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

        if (Color.TryParse(configSnapshot.PreviewHighlightColor, out Color highlightColor))
            WindowLabel.Foreground = new SolidColorBrush(highlightColor);

        ApplyPreviewBorderAppearance();

        _showFloatingWindow = configSnapshot.ShowFloatingWindows;
        ApplyVisibility();

        if (refreshPreviewPipeline)
            _service.ApplySettings();
    }

    private void ApplyPreviewBorderAppearance()
    {
        PreviewBorder.BorderThickness = new Thickness(_isSelectionPending ? 4 : 2);
        PreviewBorder.BorderBrush = _isSelectionPending
            ? _pendingSelectionBorderBrush
            : WindowLabel.Foreground;
    }

    private static IBrush ResolvePendingSelectionBorderBrush()
    {
        if (
            Application.Current?.TryFindResource(
                PendingSelectionBorderBrushKey,
                out object? resource
            ) == true
            && resource is IBrush brush
        )
        {
            return brush;
        }

        return DefaultPendingSelectionBorderBrush;
    }
}
