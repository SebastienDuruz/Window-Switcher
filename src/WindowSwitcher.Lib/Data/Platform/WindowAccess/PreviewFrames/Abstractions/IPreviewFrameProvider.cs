using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

public interface IPreviewFrameProvider : IDisposable, IAsyncDisposable
{
    Task<Bitmap?> RequestAsync(
        string windowId,
        ScreenshotRequest request,
        CancellationToken cancellationToken = default
    );
    void ForgetWindow(string windowId);
}
