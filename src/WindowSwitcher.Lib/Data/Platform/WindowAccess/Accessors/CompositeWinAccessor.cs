using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

/// <summary>
/// Combines XWayland windows listed through EWMH with native Wayland windows listed by KWin,
/// and routes each operation to the accessor that owns the window identifier.
/// </summary>
internal sealed class CompositeWinAccessor : WinAccessorBase, IAsyncDisposable
{
    private readonly WinAccessorBase _x11Accessor;
    private readonly WinAccessorBase _nativeAccessor;
    private bool _disposed;

    internal CompositeWinAccessor(WinAccessorBase x11Accessor, WinAccessorBase nativeAccessor)
    {
        ArgumentNullException.ThrowIfNull(x11Accessor);
        ArgumentNullException.ThrowIfNull(nativeAccessor);
        _x11Accessor = x11Accessor;
        _nativeAccessor = nativeAccessor;
    }

    public override async Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<IReadOnlyCollection<WindowConfig>?> x11Windows = _x11Accessor.TryGetWindowsAsync(
            cancellationToken
        );
        Task<IReadOnlyCollection<WindowConfig>?> nativeWindows = _nativeAccessor.TryGetWindowsAsync(
            cancellationToken
        );
        await Task.WhenAll(x11Windows, nativeWindows).ConfigureAwait(false);

        // One unavailable source must not hide the windows of the other one.
        IReadOnlyCollection<WindowConfig>? x11Result = await x11Windows.ConfigureAwait(false);
        IReadOnlyCollection<WindowConfig>? nativeResult = await nativeWindows.ConfigureAwait(false);
        if (x11Result is null && nativeResult is null)
            return null;

        return [.. x11Result ?? [], .. nativeResult ?? []];
    }

    public override Task<bool> TryActivateWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default
    )
    {
        return Route(windowId).TryActivateWindowAsync(windowId, cancellationToken);
    }

    public override Task<bool> TryRenameWindowAsync(
        string windowId,
        string windowTitle,
        CancellationToken cancellationToken = default
    )
    {
        return Route(windowId).TryRenameWindowAsync(windowId, windowTitle, cancellationToken);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _x11Accessor.Dispose();
        _nativeAccessor.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _x11Accessor.Dispose();
        if (_nativeAccessor is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            _nativeAccessor.Dispose();
    }

    private WinAccessorBase Route(string windowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return KWinWaylandWindowAccessor.IsKWinWindowId(windowId) ? _nativeAccessor : _x11Accessor;
    }
}
