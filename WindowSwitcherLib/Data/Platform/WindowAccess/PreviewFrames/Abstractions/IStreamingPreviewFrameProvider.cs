using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Provides continuous preview frames for a window.
/// </summary>
public interface IStreamingPreviewFrameProvider
{
    /// <summary>
    /// Streams frames as soon as they are produced by the backend.
    /// </summary>
    /// <param name="windowId">Platform-specific window id.</param>
    /// <param name="request">Requested frame constraints.</param>
    /// <param name="cancellationToken">Cancellation token for the stream lifetime.</param>
    /// <returns>Async sequence of frames. The caller owns and must dispose each bitmap.</returns>
    IAsyncEnumerable<Bitmap> StreamAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default);
}
