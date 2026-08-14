using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

public interface IPreviewFrameProvider : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Streams disposable native BGRA frames for the specified window.
    /// </summary>
    /// <remarks>The caller owns every returned frame and must dispose it after copying.</remarks>
    IAsyncEnumerable<NativeBgraPreviewFrame> StreamAsync(
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
