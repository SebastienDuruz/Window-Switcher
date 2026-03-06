using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// No-op dependency notification adapter for platforms without Linux-style dependencies.
/// </summary>
public sealed class NoOpDependencyNotificationService : IDependencyNotificationService
{
    public event Action<string>? DependencyMissing
    {
        add { }
        remove { }
    }

    public IReadOnlyCollection<string> GetReportedMissing()
    {
        return Array.Empty<string>();
    }
}
