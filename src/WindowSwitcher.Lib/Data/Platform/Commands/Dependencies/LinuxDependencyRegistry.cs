using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

/// <summary>
/// Caches Linux dependency availability and records missing dependencies once.
/// </summary>
public sealed class LinuxDependencyRegistry(
    ICommandWrapper? whichWrapper = null,
    ICommandWrapper? gstInspectWrapper = null
) : ILinuxDependencyRegistry
{
    private const string WmctrlBinary = "wmctrl";

    private readonly object _syncRoot = new();
    private readonly HashSet<string> _reportedMissing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _binaryAvailabilityCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ICommandWrapper _whichWrapper = ResolveWhichWrapper(
        whichWrapper,
        gstInspectWrapper
    );

    public event Action<string>? DependencyMissing;

    /// <inheritdoc />
    public bool IsWmctrlAvailable => CheckBinaryCached(WmctrlBinary);

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

    private bool CheckBinaryCached(string dependency)
    {
        lock (_syncRoot)
        {
            if (_binaryAvailabilityCache.TryGetValue(dependency, out bool available))
                return available;

            available = IsBinaryAvailable(dependency);
            _binaryAvailabilityCache[dependency] = available;
            return available;
        }
    }

    private bool IsBinaryAvailable(string dependency)
    {
        string output = _whichWrapper.Execute(dependency);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static ICommandWrapper ResolveWhichWrapper(
        ICommandWrapper? wrapper,
        ICommandWrapper? legacyCompatibilityArgument
    )
    {
        _ = legacyCompatibilityArgument;
        return wrapper ?? new WhichWrapper();
    }
}
