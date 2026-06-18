using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowSwitcher.Diagnostics;

internal interface IAppTelemetry
{
    void Initialize();

    Task RecordAppStartedAsync(string previewMode);

    void CaptureUnhandledException(Exception exception, string source);

    void CaptureHandledException(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string>? tags = null
    );

    Task ShutdownAsync();
}
