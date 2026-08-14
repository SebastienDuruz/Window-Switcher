using System.Runtime.CompilerServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;

internal sealed class NoOpPreviewFrameProvider : IPreviewFrameProvider
{
    public async IAsyncEnumerable<NativeBgraPreviewFrame> StreamAsync(
        string windowId,
        ScreenshotRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public void SuspendWindow(string windowId) { }

    public void ForgetWindow(string windowId) { }

    public void Dispose() { }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
