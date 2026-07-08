using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowSwitcher.Diagnostics;

internal sealed class NoOpAppTelemetry : IAppTelemetry
{
    public void Initialize() { }

    public Task RecordAppStartedAsync(string previewMode)
    {
        return Task.CompletedTask;
    }

    public void CaptureUnhandledException(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    public void CaptureHandledException(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }
}
