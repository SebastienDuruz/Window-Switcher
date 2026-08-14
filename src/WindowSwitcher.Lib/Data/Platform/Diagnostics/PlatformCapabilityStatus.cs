using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

/// <summary>
/// Immutable snapshot of the selected platform integrations.
/// </summary>
public sealed record PlatformCapabilitySnapshot(
    LinuxSessionKind Session,
    string WindowBackend,
    string PreviewBackend,
    bool PreviewAvailable,
    string? LastFailure
);

/// <summary>
/// Stores the current platform capability selection for diagnostics.
/// </summary>
public sealed class PlatformCapabilityStatus
{
    private readonly object _syncRoot = new();
    private PlatformCapabilitySnapshot _current = new(
        LinuxSessionKind.Unsupported,
        "Unavailable",
        "None",
        false,
        "Platform capabilities have not been evaluated."
    );

    /// <summary>
    /// Gets the latest immutable capability snapshot.
    /// </summary>
    public PlatformCapabilitySnapshot Current
    {
        get
        {
            lock (_syncRoot)
                return _current;
        }
    }

    /// <summary>
    /// Replaces the current capability snapshot.
    /// </summary>
    public void Update(PlatformCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_syncRoot)
            _current = snapshot;
    }

    /// <summary>
    /// Records a non-sensitive failure reason for the current selection.
    /// </summary>
    public void ReportFailure(string failure)
    {
        if (string.IsNullOrWhiteSpace(failure))
            return;

        lock (_syncRoot)
            _current = _current with { LastFailure = failure.Trim() };
    }
}
