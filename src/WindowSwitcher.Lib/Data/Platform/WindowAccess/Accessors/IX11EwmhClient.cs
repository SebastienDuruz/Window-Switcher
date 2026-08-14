namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

internal interface IX11EwmhClient : IDisposable
{
    IReadOnlyList<X11EwmhWindow> GetWindows();

    bool TryActivateWindow(uint windowId);

    bool TryRenameWindow(uint windowId, string title);
}

internal readonly record struct X11EwmhWindow(uint WindowId, uint ProcessId, string Title);
