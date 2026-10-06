using Serilog.Events;
using Serilog.Parsing;
using WindowSwitcher.Lib.Data.Diagnostics.Logging;
using Xunit;

namespace WindowSwitcher.Tests.Diagnostics;

public sealed class RepeatedLogEventFilterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    [Fact]
    public void IsEnabled_DropsIdenticalEventWithinWindow()
    {
        var clock = new ManualTimeProvider();
        var sut = new RepeatedLogEventFilter(clock, Window, maximumTrackedSignatures: 8);

        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "capture failed")));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "capture failed")));
    }

    [Fact]
    public void IsEnabled_ReportsIdenticalEventAgainAfterWindow()
    {
        var clock = new ManualTimeProvider();
        var sut = new RepeatedLogEventFilter(clock, Window, maximumTrackedSignatures: 8);

        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Warning, "portal timeout")));
        clock.Advance(Window);
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Warning, "portal timeout")));
    }

    [Fact]
    public void IsEnabled_DistinguishesLevelMessageAndExceptionType()
    {
        var clock = new ManualTimeProvider();
        var sut = new RepeatedLogEventFilter(clock, Window, maximumTrackedSignatures: 8);

        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Warning, "same")));
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "same")));
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "other")));
        Assert.True(
            sut.IsEnabled(CreateEvent(LogEventLevel.Error, "same", new InvalidOperationException()))
        );
        Assert.True(
            sut.IsEnabled(CreateEvent(LogEventLevel.Error, "same", new TimeoutException()))
        );
        Assert.False(
            sut.IsEnabled(CreateEvent(LogEventLevel.Error, "same", new TimeoutException()))
        );
    }

    [Fact]
    public void IsEnabled_EvictsOldestSignatureWhenLimitIsReached()
    {
        var clock = new ManualTimeProvider();
        var sut = new RepeatedLogEventFilter(clock, Window, maximumTrackedSignatures: 2);

        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "first")));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "second")));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "third")));

        // "first" was evicted to make room for "third", so it is reported again.
        Assert.True(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "first")));
        Assert.False(sut.IsEnabled(CreateEvent(LogEventLevel.Error, "third")));
    }

    [Fact]
    public void Constructor_RejectsInvalidLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepeatedLogEventFilter(TimeProvider.System, TimeSpan.Zero, 1)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepeatedLogEventFilter(TimeProvider.System, Window, 0)
        );
    }

    private static LogEvent CreateEvent(
        LogEventLevel level,
        string message,
        Exception? exception = null
    )
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            new MessageTemplateParser().Parse(message),
            []
        );
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
