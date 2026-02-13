using WindowSwitcherLib.Application.Platform;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// Linux dependency notification adapter backed by <see cref="LinuxDependencies"/>.
/// </summary>
public sealed class LinuxDependencyNotificationService : IDependencyNotificationService
{
    public event Action<string>? DependencyMissing
    {
        add => LinuxDependencies.DependencyMissing += value;
        remove => LinuxDependencies.DependencyMissing -= value;
    }

    public IReadOnlyCollection<string> GetReportedMissing()
    {
        return LinuxDependencies.GetReportedMissing();
    }
}
