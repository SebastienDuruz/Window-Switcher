using Serilog;
using Serilog.Events;

namespace WindowSwitcher.Tests.TestLogging;

internal static class TestLogger
{
    public static Serilog.ILogger Create(out CollectingSink sink)
    {
        sink = new CollectingSink();
        return new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Verbose)
            .WriteTo.Sink(sink)
            .CreateLogger();
    }
}
