using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class X11PreviewFrameProviderTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StreamAsync_RetriesSkippedCaptureWithoutNewDamageOrNewSession()
    {
        var session = new FakeCaptureSession(
            CaptureOutcome.Frame,
            CaptureOutcome.Skipped,
            CaptureOutcome.Frame
        );
        session.QueueDamage();
        int createdSessions = 0;
        await using var provider = new X11PreviewFrameProvider(_ =>
        {
            createdSessions++;
            return session;
        });

        await using IAsyncEnumerator<PreviewFrame> frames = provider
            .StreamAsync("0x00000042", new ScreenshotRequest())
            .GetAsyncEnumerator();

        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
        frames.Current.Dispose();
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
        frames.Current.Dispose();

        Assert.Equal(3, session.CaptureCount);
        Assert.Equal(1, session.ConsumedDamageCount);
        Assert.Equal(1, createdSessions);
        Assert.False(session.IsDisposed);
    }

    [Fact]
    public async Task StreamAsync_RemovesSessionWhenCaptureFails()
    {
        var failedSession = new FakeCaptureSession(CaptureOutcome.Failed);
        var replacementSession = new FakeCaptureSession(CaptureOutcome.Frame);
        var sessions = new Queue<FakeCaptureSession>([failedSession, replacementSession]);
        await using var provider = new X11PreviewFrameProvider(_ => sessions.Dequeue());

        await using (
            IAsyncEnumerator<PreviewFrame> failedFrames = provider
                .StreamAsync("0x00000042", new ScreenshotRequest())
                .GetAsyncEnumerator()
        )
        {
            Assert.False(await failedFrames.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
        }

        await using IAsyncEnumerator<PreviewFrame> replacementFrames = provider
            .StreamAsync("0x00000042", new ScreenshotRequest())
            .GetAsyncEnumerator();
        Assert.True(await replacementFrames.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
        replacementFrames.Current.Dispose();

        Assert.True(failedSession.IsDisposed);
        Assert.False(replacementSession.IsDisposed);
    }

    private enum CaptureOutcome
    {
        Frame,
        Skipped,
        Failed,
    }

    private sealed class FakeCaptureSession(params CaptureOutcome[] outcomes)
        : IX11WindowCaptureSession
    {
        private readonly Queue<CaptureOutcome> _outcomes = new(outcomes);
        private int _pendingDamage;

        internal int CaptureCount { get; private set; }
        internal int ConsumedDamageCount { get; private set; }
        internal bool IsDisposed { get; private set; }

        internal void QueueDamage()
        {
            _pendingDamage++;
        }

        public async Task<bool> WaitForDamageAsync(
            int timeoutMs,
            CancellationToken cancellationToken
        )
        {
            if (_pendingDamage > 0)
            {
                _pendingDamage--;
                ConsumedDamageCount++;
                return true;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }

        public bool TryCaptureFrame(ScreenshotRequest request, out NativeBgraPreviewFrame? frame)
        {
            CaptureCount++;
            frame = null;
            CaptureOutcome outcome =
                _outcomes.Count > 0 ? _outcomes.Dequeue() : CaptureOutcome.Failed;
            if (outcome == CaptureOutcome.Failed)
                return false;
            if (outcome == CaptureOutcome.Frame)
                frame = new NativeBgraPreviewFrame(IntPtr.Zero, 4, 1, 1, 4, static () => { });
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
