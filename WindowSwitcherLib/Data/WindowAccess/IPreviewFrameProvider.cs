using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public interface IPreviewFrameProvider : IDisposable, IAsyncDisposable
{
    Task<Bitmap?> RequestAsync(string windowId, ScreenshotRequest request, CancellationToken cancellationToken = default);
    void ForgetWindow(string windowId);
}
