using Serilog.Events;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Tests.TestLogging;
using WindowSwitcher.Windows.Services.Preview;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class LatestPreviewFrameQueueTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Frames_AreDisplayedAndHandedOver()
    {
        var display = new RecordingDisplay();
        LatestPreviewFrameQueue sut = CreateSut(display);
        var frame = new TestFrame("A");

        sut.Enqueue(frame, CancellationToken.None);

        await display.WaitForAsync(1);
        Assert.Equal(["A"], display.Displayed);
        Assert.False(frame.IsDisposed); // the display callback owns displayed frames
    }

    [Fact]
    public async Task FramesArrivingDuringADisplay_KeepOnlyTheLatest()
    {
        var display = new RecordingDisplay { BlockFirstFrame = true };
        LatestPreviewFrameQueue sut = CreateSut(display);
        var first = new TestFrame("first");
        var skipped = new TestFrame("skipped");
        var latest = new TestFrame("latest");

        sut.Enqueue(first, CancellationToken.None);
        await display.FirstFrameStarted.Task.WaitAsync(WaitTimeout);
        sut.Enqueue(skipped, CancellationToken.None);
        sut.Enqueue(latest, CancellationToken.None);
        display.ReleaseFirstFrame();

        await display.WaitForAsync(2);
        Assert.Equal(["first", "latest"], display.Displayed);
        Assert.True(skipped.IsDisposed);
        Assert.Equal(1, display.MaximumConcurrency);
    }

    [Fact]
    public void RejectedFrames_AreDisposedImmediately()
    {
        var display = new RecordingDisplay();
        bool accepts = false;
        var sut = new LatestPreviewFrameQueue(
            display.ApplyAsync,
            () => accepts,
            () => false,
            Serilog.Core.Logger.None
        );
        var whileDisabled = new TestFrame("disabled");
        var afterCancellation = new TestFrame("cancelled");

        sut.Enqueue(whileDisabled, CancellationToken.None);
        accepts = true;
        sut.Enqueue(afterCancellation, new CancellationToken(canceled: true));

        Assert.True(whileDisabled.IsDisposed);
        Assert.True(afterCancellation.IsDisposed);
        Assert.Empty(display.Displayed);
    }

    [Fact]
    public async Task DiscardPending_DisposesTheWaitingFrame()
    {
        var display = new RecordingDisplay { BlockFirstFrame = true };
        LatestPreviewFrameQueue sut = CreateSut(display);
        sut.Enqueue(new TestFrame("first"), CancellationToken.None);
        await display.FirstFrameStarted.Task.WaitAsync(WaitTimeout);
        var waiting = new TestFrame("waiting");
        sut.Enqueue(waiting, CancellationToken.None);

        sut.DiscardPending();
        display.ReleaseFirstFrame();

        Assert.True(waiting.IsDisposed);
        await Task.Delay(50);
        Assert.Equal(["first"], display.Displayed);
    }

    [Fact]
    public async Task CancellationDuringADisplay_DisposesTheWaitingFrame()
    {
        var display = new RecordingDisplay { BlockFirstFrame = true };
        LatestPreviewFrameQueue sut = CreateSut(display);
        using var cancellation = new CancellationTokenSource();
        sut.Enqueue(new TestFrame("first"), cancellation.Token);
        await display.FirstFrameStarted.Task.WaitAsync(WaitTimeout);
        var waiting = new TestFrame("waiting");
        sut.Enqueue(waiting, cancellation.Token);

        await cancellation.CancelAsync();
        display.ReleaseFirstFrame();

        await WaitUntilAsync(() => waiting.IsDisposed);
        Assert.Equal(["first"], display.Displayed);
    }

    [Fact]
    public async Task DisplayFailure_IsLoggedAndLaterFramesStillDisplay()
    {
        var display = new RecordingDisplay { FailingFrame = "broken" };
        var sut = new LatestPreviewFrameQueue(
            display.ApplyAsync,
            () => true,
            () => false,
            TestLogger.Create(out CollectingSink logs)
        );

        sut.Enqueue(new TestFrame("broken"), CancellationToken.None);
        await WaitUntilAsync(() => logs.Contains(LogEventLevel.Error, "could not be displayed"));
        sut.Enqueue(new TestFrame("next"), CancellationToken.None);

        await display.WaitForAsync(1);
        Assert.Equal(["next"], display.Displayed);
    }

    private static LatestPreviewFrameQueue CreateSut(RecordingDisplay display) =>
        new(display.ApplyAsync, () => true, () => false, Serilog.Core.Logger.None);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }

    private sealed class TestFrame(string name) : PreviewFrame
    {
        public string Name { get; } = name;
        public bool IsDisposed { get; private set; }
        public override int WidthPx => 1;
        public override int HeightPx => 1;

        public override void Dispose() => IsDisposed = true;
    }

    private sealed class RecordingDisplay
    {
        private readonly Lock _sync = new();
        private readonly List<string> _displayed = [];
        private readonly TaskCompletionSource _releaseFirstFrame = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _active;
        private int _calls;

        public bool BlockFirstFrame { get; init; }
        public string? FailingFrame { get; init; }
        public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource FirstFrameStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Displayed
        {
            get
            {
                lock (_sync)
                    return _displayed.ToArray();
            }
        }

        public void ReleaseFirstFrame() => _releaseFirstFrame.TrySetResult();

        public async Task ApplyAsync(PreviewFrame frame, CancellationToken cancellationToken)
        {
            var testFrame = (TestFrame)frame;
            int active = Interlocked.Increment(ref _active);
            lock (_sync)
                MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try
            {
                if (testFrame.Name == FailingFrame)
                    throw new InvalidOperationException("bitmap allocation failed");
                if (Interlocked.Increment(ref _calls) == 1 && BlockFirstFrame)
                {
                    FirstFrameStarted.TrySetResult();
                    await _releaseFirstFrame.Task;
                }
                lock (_sync)
                    _displayed.Add(testFrame.Name);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task WaitForAsync(int count) => WaitUntilAsync(() => Displayed.Count >= count);
    }
}

public sealed class DebouncedRefreshTests
{
    [Fact]
    public async Task BurstOfRequests_RunsOneRefreshAfterTheQuietPeriod()
    {
        int refreshes = 0;
        // Generous margins: CI runners can delay timers by tens of milliseconds.
        var sut = new DebouncedRefresh(
            TimeSpan.FromMilliseconds(300),
            () => Interlocked.Increment(ref refreshes),
            () => false
        );

        for (int i = 0; i < 5; i++)
            sut.Schedule();
        Assert.Equal(0, Volatile.Read(ref refreshes));

        await Task.Delay(1000);
        Assert.Equal(1, Volatile.Read(ref refreshes));
    }

    [Fact]
    public async Task Flush_RunsThePendingRefreshImmediatelyAndOnlyOnce()
    {
        int refreshes = 0;
        var sut = new DebouncedRefresh(
            TimeSpan.FromMilliseconds(50),
            () => Interlocked.Increment(ref refreshes),
            () => false
        );
        sut.Schedule();

        sut.Flush();
        sut.Flush();

        Assert.Equal(1, Volatile.Read(ref refreshes));
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref refreshes));
    }

    [Fact]
    public async Task Cancel_DropsThePendingRefresh()
    {
        int refreshes = 0;
        var sut = new DebouncedRefresh(
            TimeSpan.FromMilliseconds(50),
            () => Interlocked.Increment(ref refreshes),
            () => false
        );
        sut.Schedule();

        sut.Cancel();
        sut.Flush();

        await Task.Delay(200);
        Assert.Equal(0, Volatile.Read(ref refreshes));
    }

    [Fact]
    public async Task ClosingWindow_NeverRunsTheRefresh()
    {
        int refreshes = 0;
        bool closing = false;
        var sut = new DebouncedRefresh(
            TimeSpan.FromMilliseconds(20),
            () => Interlocked.Increment(ref refreshes),
            () => closing
        );

        sut.Schedule();
        closing = true;
        await Task.Delay(150);
        sut.Schedule();
        sut.Flush();

        Assert.Equal(0, Volatile.Read(ref refreshes));
    }

    [Fact]
    public void Flush_WithoutPendingRequest_DoesNothing()
    {
        int refreshes = 0;
        var sut = new DebouncedRefresh(
            TimeSpan.FromMilliseconds(20),
            () => refreshes++,
            () => false
        );

        sut.Flush();

        Assert.Equal(0, refreshes);
    }
}
