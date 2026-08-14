using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

/// <summary>
/// Global Linux dependency access used by runtime Linux integrations.
/// </summary>
public static class LinuxDependencies
{
    private static readonly LinuxDependencyRegistry Registry = new();

    /// <summary>
    /// Gets the shared dependency registry for injectable runtime components.
    /// </summary>
    public static ILinuxDependencyRegistry Instance => Registry;

    /// <summary>
    /// Raised when a dependency is reported missing for the first time.
    /// </summary>
    public static event Action<string>? DependencyMissing
    {
        add => Registry.DependencyMissing += value;
        remove => Registry.DependencyMissing -= value;
    }

    /// <summary>
    /// Returns missing dependencies already reported to the application.
    /// </summary>
    public static IReadOnlyCollection<string> GetReportedMissing()
    {
        return Registry.GetReportedMissing();
    }

    /// <summary>
    /// Records a missing dependency so it is only reported once.
    /// </summary>
    public static void ReportMissingOnce(string dependency)
    {
        Registry.ReportMissingOnce(dependency);
    }
}
