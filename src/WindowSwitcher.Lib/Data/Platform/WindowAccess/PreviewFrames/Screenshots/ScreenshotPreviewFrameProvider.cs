using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Screenshots;

public sealed class ScreenshotPreviewFrameProvider : IPreviewFrameProvider
{
    private readonly ScreenshotQueue _screenshotQueue;

    public ScreenshotPreviewFrameProvider(WinAccessorBase accessorBase)
    {
        ArgumentNullException.ThrowIfNull(accessorBase);
        _screenshotQueue = new ScreenshotQueue(accessorBase);
    }

    public Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return _screenshotQueue.RequestAsync(windowId, request, cancellationToken);
    }

    public void ForgetWindow(string windowId)
    {
        _screenshotQueue.ForgetWindow(windowId);
    }

    public void Dispose()
    {
        _screenshotQueue.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _screenshotQueue.DisposeAsync();
    }
}
