using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;

internal sealed class NoOpPreviewFrameProvider : IPreviewFrameProvider
{
    public Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Bitmap?>(null);
    }

    public void SuspendWindow(string windowId) { }

    public void ForgetWindow(string windowId) { }

    public void Dispose() { }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
