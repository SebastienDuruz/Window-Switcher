using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

/// <summary>
/// Records missing Linux dependencies once for user-facing diagnostics.
/// </summary>
public sealed class LinuxDependencyRegistry : ILinuxDependencyRegistry
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _reportedMissing = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? DependencyMissing;

    /// <summary>
    /// Returns missing dependencies already reported to the application.
    /// </summary>
    public IReadOnlyCollection<string> GetReportedMissing()
    {
        lock (_syncRoot)
        {
            return _reportedMissing.ToArray();
        }
    }

    /// <inheritdoc />
    public void ReportMissingOnce(string dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency))
            return;

        bool shouldReport;
        lock (_syncRoot)
        {
            shouldReport = _reportedMissing.Add(dependency);
        }

        if (!shouldReport)
            return;

        DependencyMissing?.Invoke(dependency);
    }
}
