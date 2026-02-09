using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public sealed class ScreenshotPreviewFrameProvider : IPreviewFrameProvider
{
    private readonly ScreenshotQueue _screenshotQueue;

    public ScreenshotPreviewFrameProvider(WinAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _screenshotQueue = new ScreenshotQueue(accessor);
    }

    public Task<Bitmap?> RequestAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken = default)
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
