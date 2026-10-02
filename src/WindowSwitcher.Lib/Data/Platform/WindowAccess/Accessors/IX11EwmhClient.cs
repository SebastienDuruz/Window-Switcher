namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

internal interface IX11EwmhClient : IDisposable
{
    /// <summary>
    /// Reads the EWMH client list.
    /// </summary>
    /// <returns>The client windows, or <see langword="null" /> when the list cannot be read.</returns>
    IReadOnlyList<X11EwmhWindow>? TryGetWindows();

    bool TryActivateWindow(uint windowId);

    bool TryRenameWindow(uint windowId, string title);
}

internal readonly record struct X11EwmhWindow(uint WindowId, uint ProcessId, string Title);
