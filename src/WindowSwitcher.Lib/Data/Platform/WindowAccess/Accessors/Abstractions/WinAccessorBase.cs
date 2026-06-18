using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;

public abstract class WinAccessorBase
{
    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public abstract ObservableCollection<WindowConfig> GetWindows();

    /// <summary>
    /// Asynchronously get the list of windows currently opened.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the window query.</param>
    /// <returns>The current window snapshot.</returns>
    public virtual Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<WindowConfig>>(GetWindows());
    }

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowId">The id of the window to raise</param>
    public abstract void RaiseWindow(string windowId);

    /// <summary>
    /// Asynchronously raise the window to the front.
    /// </summary>
    /// <param name="windowId">The id of the window to raise.</param>
    /// <param name="cancellationToken">Token used to cancel the raise operation.</param>
    public virtual Task RaiseWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        RaiseWindow(windowId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Take a screenshot of a window
    /// </summary>
    /// <param name="window">The window to screenshot</param>
    public abstract Bitmap? TakeScreenshot(string windowId);

    /// <summary>
    /// Take a screenshot of a window with additional constraints (e.g. max size, timeout).
    /// Default implementation falls back to <see cref="TakeScreenshot(string)"/>.
    /// </summary>
    public virtual Bitmap? TakeScreenshot(string windowId, ScreenshotRequest request) =>
        TakeScreenshot(windowId);

    /// <summary>
    /// Async screenshot capture. Default implementation wraps the synchronous API.
    /// </summary>
    public virtual Task<Bitmap?> TakeScreenshotAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(TakeScreenshot(windowId, request));

    /// <summary>
    /// Rename a window title
    /// </summary>
    /// <param name="windowId">The window id to rename</param>
    /// <param name="windowTitle">The new window title</param>
    public abstract void RenameWindowTitle(string windowId, string windowTitle);

    /// <summary>
    /// Asynchronously rename a window title.
    /// </summary>
    /// <param name="windowId">The window id to rename.</param>
    /// <param name="windowTitle">The new window title.</param>
    /// <param name="cancellationToken">Token used to cancel the rename operation.</param>
    public virtual Task RenameWindowTitleAsync(
        string windowId,
        string windowTitle,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenameWindowTitle(windowId, windowTitle);
        return Task.CompletedTask;
    }
}
