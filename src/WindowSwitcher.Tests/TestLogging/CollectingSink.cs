using Serilog.Core;
using Serilog.Events;

namespace WindowSwitcher.Tests.TestLogging;

internal sealed class CollectingSink : ILogEventSink
{
    private readonly object _syncRoot = new();
    private readonly List<LogEvent> _events = [];

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_syncRoot)
                return _events.ToArray();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_syncRoot)
            _events.Add(logEvent);
    }

    public IReadOnlyList<LogEvent> AtLevel(LogEventLevel level)
    {
        return Events.Where(logEvent => logEvent.Level == level).ToArray();
    }

    public bool Contains(LogEventLevel level, string renderedMessageFragment)
    {
        return Events.Any(logEvent =>
            logEvent.Level == level
            && logEvent.RenderMessage().Contains(renderedMessageFragment, StringComparison.Ordinal)
        );
    }
}
