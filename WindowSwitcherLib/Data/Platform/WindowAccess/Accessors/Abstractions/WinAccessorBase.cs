using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Domain.Models;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;

public abstract class WinAccessorBase
{
    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public abstract ObservableCollection<WindowConfig> GetWindows();

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowPID">The PID of the window to raise</param>
    public abstract void RaiseWindow(string windowId);

    /// <summary>
    /// Take a screenshot of a window
    /// </summary>
    /// <param name="window">The window to screenshot</param>
    public abstract Bitmap? TakeScreenshot(string windowId);

    /// <summary>
    /// Take a screenshot of a window with additional constraints (e.g. max size, timeout).
    /// Default implementation falls back to <see cref="TakeScreenshot(string)"/>.
    /// </summary>
    public virtual Bitmap? TakeScreenshot(string windowId, ScreenshotRequest request) => TakeScreenshot(windowId);

    /// <summary>
    /// Async screenshot capture. Default implementation wraps the synchronous API.
    /// </summary>
    public virtual Task<Bitmap?> TakeScreenshotAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(TakeScreenshot(windowId, request));

    /// <summary>
    /// Rename a window title
    /// </summary>
    /// <param name="windowId">The window id to rename</param>
    /// <param name="windowTitle">The new window title</param>
    public abstract void RenameWindowTitle(string windowId, string windowTitle);
}
