using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

public interface IPreviewFrameProvider : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Requests a preview frame for the specified window.
    /// </summary>
    Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Suspends active preview work for the specified window while keeping any reusable session state.
    /// </summary>
    void SuspendWindow(string windowId);

    /// <summary>
    /// Forgets all preview state associated with the specified window.
    /// </summary>
    void ForgetWindow(string windowId);
}
