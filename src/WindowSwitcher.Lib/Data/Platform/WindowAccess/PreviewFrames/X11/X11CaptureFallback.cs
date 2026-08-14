using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

internal static class X11CaptureFallback
{
    internal static NativeBgraPreviewFrame? Capture(
        bool hasSharedMemoryImage,
        Func<X11CaptureAttempt> captureSharedMemory,
        Action disableSharedMemory,
        Func<NativeBgraPreviewFrame?> captureXImage
    )
    {
        ArgumentNullException.ThrowIfNull(captureSharedMemory);
        ArgumentNullException.ThrowIfNull(disableSharedMemory);
        ArgumentNullException.ThrowIfNull(captureXImage);

        if (!hasSharedMemoryImage)
            return captureXImage();

        X11CaptureAttempt attempt = captureSharedMemory();
        if (attempt.BackendSucceeded)
            return attempt.Frame;

        disableSharedMemory();
        return captureXImage();
    }
}

internal readonly record struct X11CaptureAttempt(
    bool BackendSucceeded,
    NativeBgraPreviewFrame? Frame
);
