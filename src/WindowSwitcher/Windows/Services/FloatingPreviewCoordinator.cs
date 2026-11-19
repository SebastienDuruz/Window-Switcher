using System;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.ViewModels.Abstractions;
using WindowSwitcher.Windows.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class FloatingPreviewCoordinator : IDisposable
{
    private readonly IWindowKeybindActivator _windowKeybindActivator;
    private readonly IViewModelDispatcher _dispatcher;
    private readonly Action<string> _selectPreviewByWindowId;
    private readonly Func<bool> _isHideActivePreviewEnabled;
    private IFloatingPreviewWindow? _activePreviewWindow;
    private IFloatingPreviewWindow? _hiddenPreviewWindow;

    public FloatingPreviewCoordinator(
        IWindowKeybindActivator windowKeybindActivator,
        IViewModelDispatcher dispatcher,
        Action<string> selectPreviewByWindowId,
        Func<bool> isHideActivePreviewEnabled
    )
    {
        ArgumentNullException.ThrowIfNull(windowKeybindActivator);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(selectPreviewByWindowId);
        ArgumentNullException.ThrowIfNull(isHideActivePreviewEnabled);

        _windowKeybindActivator = windowKeybindActivator;
        _dispatcher = dispatcher;
        _selectPreviewByWindowId = selectPreviewByWindowId;
        _isHideActivePreviewEnabled = isHideActivePreviewEnabled;
        _windowKeybindActivator.WindowActivated += OnWindowKeybindActivated;
    }

    public void SetActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        ArgumentNullException.ThrowIfNull(floatingWindow);

        if (_activePreviewWindow == floatingWindow)
            return;

        _activePreviewWindow?.SetPreviewHighlight(false);
        _activePreviewWindow = floatingWindow;
        _activePreviewWindow.SetPreviewHighlight(true);
    }

    public void ClearActivePreview(IFloatingPreviewWindow floatingWindow)
    {
        ArgumentNullException.ThrowIfNull(floatingWindow);

        // A closed window must not be shown again on the next activation.
        if (_hiddenPreviewWindow == floatingWindow)
            _hiddenPreviewWindow = null;

        if (_activePreviewWindow != floatingWindow)
            return;

        _activePreviewWindow.SetPreviewHighlight(false);
        _activePreviewWindow = null;
    }

    public void NotifyPreviewWindowActivated(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _windowKeybindActivator.NotifyWindowActivated(windowId);
        UpdateActivePreviewVisibility();
    }

    /// <summary>
    /// Shows the hidden preview again when "Hide active preview" has just been disabled.
    /// Enabling the setting hides nothing until the next activation.
    /// </summary>
    public void ApplySettings()
    {
        if (_hiddenPreviewWindow is null || _isHideActivePreviewEnabled())
            return;

        IFloatingPreviewWindow previewToShow = _hiddenPreviewWindow;
        _hiddenPreviewWindow = null;
        previewToShow.SetPreviewHidden(false);
    }

    public void Dispose()
    {
        _windowKeybindActivator.WindowActivated -= OnWindowKeybindActivated;
        _activePreviewWindow = null;
        _hiddenPreviewWindow = null;
    }

    private void OnWindowKeybindActivated(object? sender, string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _ = _dispatcher.InvokeAsync(() =>
        {
            _selectPreviewByWindowId(windowId);
            UpdateActivePreviewVisibility();
        });
    }

    private void UpdateActivePreviewVisibility()
    {
        IFloatingPreviewWindow? previewToHide = _isHideActivePreviewEnabled()
            ? _activePreviewWindow
            : null;
        if (previewToHide == _hiddenPreviewWindow)
            return;

        IFloatingPreviewWindow? previewToShow = _hiddenPreviewWindow;
        _hiddenPreviewWindow = previewToHide;
        previewToShow?.SetPreviewHidden(false);
        previewToHide?.SetPreviewHidden(true);
    }
}
