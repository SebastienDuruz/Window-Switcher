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
    private IFloatingPreviewWindow? _activePreviewWindow;

    public FloatingPreviewCoordinator(
        IWindowKeybindActivator windowKeybindActivator,
        IViewModelDispatcher dispatcher,
        Action<string> selectPreviewByWindowId
    )
    {
        ArgumentNullException.ThrowIfNull(windowKeybindActivator);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(selectPreviewByWindowId);

        _windowKeybindActivator = windowKeybindActivator;
        _dispatcher = dispatcher;
        _selectPreviewByWindowId = selectPreviewByWindowId;
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
    }

    public void Dispose()
    {
        _windowKeybindActivator.WindowActivated -= OnWindowKeybindActivated;
        _activePreviewWindow = null;
    }

    private void OnWindowKeybindActivated(object? sender, string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        _ = _dispatcher.InvokeAsync(() => _selectPreviewByWindowId(windowId));
    }
}
