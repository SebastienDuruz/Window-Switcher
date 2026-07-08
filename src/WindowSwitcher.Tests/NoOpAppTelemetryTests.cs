using System;
using System.Threading.Tasks;
using WindowSwitcher.Diagnostics;
using Xunit;

namespace WindowSwitcher.Tests;

public sealed class NoOpAppTelemetryTests
{
    [Fact]
    public async Task Methods_CompleteWithoutCapturingTelemetry()
    {
        var sut = new NoOpAppTelemetry();

        sut.Initialize();
        await sut.RecordAppStartedAsync("PipeWire");
        sut.CaptureUnhandledException(new InvalidOperationException("boom"), "test_unhandled");
        sut.CaptureHandledException(new InvalidOperationException("boom"), "test_handled");
        await sut.ShutdownAsync();
    }
}
