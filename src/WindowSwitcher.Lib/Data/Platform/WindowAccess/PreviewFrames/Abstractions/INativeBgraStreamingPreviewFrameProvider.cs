using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Provides streaming native BGRA preview frames without copying into managed buffers.
/// </summary>
public interface INativeBgraStreamingPreviewFrameProvider
{
    /// <summary>
    /// Streams native BGRA frames for a window preview.
    /// </summary>
    /// <param name="windowId">Platform-specific window identifier.</param>
    /// <param name="request">Preview request dimensions and timeout.</param>
    /// <param name="cancellationToken">Cancellation token used to stop streaming.</param>
    /// <returns>An asynchronous stream of disposable native BGRA frame leases.</returns>
    IAsyncEnumerable<NativeBgraPreviewFrame> StreamNativeBgraAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    );
}
