using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

public interface IPreviewFrameProvider : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Streams disposable CPU or GPU-backed preview frames for the specified window.
    /// </summary>
    /// <remarks>The caller owns every returned frame and must dispose it after rendering.</remarks>
    IAsyncEnumerable<PreviewFrame> StreamAsync(
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
