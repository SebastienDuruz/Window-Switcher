using System.Diagnostics;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

internal interface IPlatformDiagnostics
{
    void Information(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

internal sealed class TracePlatformDiagnostics : IPlatformDiagnostics
{
    private static readonly TimeSpan RepetitionWindow = TimeSpan.FromSeconds(30);
    private const int MaximumTrackedSignatures = 64;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DateTimeOffset> _lastReports = new(StringComparer.Ordinal);

    internal static TracePlatformDiagnostics Instance { get; } = new();

    public void Information(string message)
    {
        if (ShouldReport($"information:{message}"))
            Trace.TraceInformation("Platform: {0}", message);
    }

    public void Warning(string message)
    {
        if (ShouldReport($"warning:{message}"))
            Trace.TraceWarning("Platform: {0}", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        string key = $"error:{message}:{exception?.GetType().FullName}";
        if (!ShouldReport(key))
            return;

        Trace.TraceError(
            exception is null ? "Platform: {0}" : "Platform: {0} ({1})",
            message,
            exception?.GetType().Name ?? string.Empty
        );
    }

    private bool ShouldReport(string key)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            if (
                _lastReports.TryGetValue(key, out DateTimeOffset previous)
                && now - previous < RepetitionWindow
            )
                return false;

            foreach (
                string expired in _lastReports
                    .Where(pair => now - pair.Value >= RepetitionWindow)
                    .Select(pair => pair.Key)
                    .ToArray()
            )
                _lastReports.Remove(expired);

            if (!_lastReports.ContainsKey(key) && _lastReports.Count >= MaximumTrackedSignatures)
            {
                string oldest = _lastReports.MinBy(pair => pair.Value).Key;
                _lastReports.Remove(oldest);
            }

            _lastReports[key] = now;
            return true;
        }
    }
}
