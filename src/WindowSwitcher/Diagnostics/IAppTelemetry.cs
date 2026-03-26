using System;
using System.Threading.Tasks;

namespace WindowSwitcher.Diagnostics;

internal interface IAppTelemetry
{
    void Initialize();

    Task RecordAppStartedAsync(string previewMode);

    void CaptureUnhandledException(Exception exception, string source);

    Task ShutdownAsync();
}
