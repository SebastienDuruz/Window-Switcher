using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Windows.Abstractions;
using WindowConfig = WindowSwitcher.Lib.Models.WindowConfig;

namespace WindowSwitcher.Windows.Services;

internal sealed class FloatingWindowRegistry
{
    private readonly WinAccessorBase _winAccessorBase;
    private readonly IPreviewFrameProvider _previewFrameProvider;
    private readonly IFloatingWindowHost _floatingWindowHost;
    private readonly Dictionary<string, FloatingWindow> _windows = new(StringComparer.Ordinal);

    public FloatingWindowRegistry(
        WinAccessorBase winAccessorBase,
        IPreviewFrameProvider previewFrameProvider,
        IFloatingWindowHost floatingWindowHost
    )
    {
        ArgumentNullException.ThrowIfNull(winAccessorBase);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        ArgumentNullException.ThrowIfNull(floatingWindowHost);

        _winAccessorBase = winAccessorBase;
        _previewFrameProvider = previewFrameProvider;
        _floatingWindowHost = floatingWindowHost;
    }

    public void Initialize(IEnumerable<WindowConfig> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        foreach (WindowConfig window in windows)
            TryAdd(window);
    }

    public void SynchronizeWithCollectionChange(
        NotifyCollectionChangedEventArgs change,
        IReadOnlyCollection<WindowConfig> currentWindows
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(currentWindows);

        if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            CloseAll();
            Initialize(currentWindows);
            return;
        }

        if (change.NewItems is not null)
        {
            foreach (WindowConfig window in change.NewItems.OfType<WindowConfig>())
                TryAdd(window);
        }

        if (change.OldItems is not null)
        {
            foreach (WindowConfig window in change.OldItems.OfType<WindowConfig>())
                Remove(window.WindowId);
        }
    }

    public bool TryGet(string windowId, out FloatingWindow floatingWindow)
    {
        if (string.IsNullOrWhiteSpace(windowId))
        {
            floatingWindow = null!;
            return false;
        }

        if (_windows.TryGetValue(windowId, out FloatingWindow? existingWindow) && existingWindow is not null)
        {
            floatingWindow = existingWindow;
            return true;
        }

        floatingWindow = null!;
        return false;
    }

    public void ApplySettings()
    {
        foreach (FloatingWindow floatingWindow in _windows.Values.ToList())
            floatingWindow.ApplySettings();
    }

    public void CloseAll()
    {
        foreach (FloatingWindow floatingWindow in _windows.Values.ToList())
            floatingWindow.RequestCloseFromHost();
        _windows.Clear();
    }

    private void TryAdd(WindowConfig window)
    {
        if (_windows.ContainsKey(window.WindowId))
            return;

        _windows[window.WindowId] = new FloatingWindow(
            window,
            _winAccessorBase,
            _previewFrameProvider,
            _floatingWindowHost
        );
    }

    private void Remove(string windowId)
    {
        if (!_windows.Remove(windowId, out FloatingWindow? floatingWindow))
            return;

        floatingWindow.RequestCloseFromHost();
    }
}
