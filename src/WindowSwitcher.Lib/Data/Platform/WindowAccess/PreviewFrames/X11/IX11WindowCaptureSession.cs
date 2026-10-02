using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal interface IX11WindowCaptureSession : IDisposable
{
    Task<bool> WaitForDamageAsync(int timeoutMs, CancellationToken cancellationToken);

    /// <summary>
    /// Captures the current window content.
    /// </summary>
    /// <param name="request">Requested preview size.</param>
    /// <param name="frame">
    /// The captured frame, or <see langword="null" /> when every session buffer is still leased
    /// and the capture was skipped.
    /// </param>
    /// <returns><see langword="false" /> when the window can no longer be captured.</returns>
    bool TryCaptureFrame(ScreenshotRequest request, out NativeBgraPreviewFrame? frame);
}
