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
    /// Gets whether <c>wmctrl</c> is available.
    /// </summary>
    public static bool IsWmctrlAvailable => Registry.IsWmctrlAvailable;

    /// <summary>
    /// Gets whether <c>import</c> is available.
    /// </summary>
    public static bool IsImportAvailable => Registry.IsImportAvailable;

    /// <summary>
    /// Gets whether <c>gst-launch-1.0</c> is available.
    /// </summary>
    public static bool IsGstLaunchAvailable => Registry.IsGstLaunchAvailable;

    /// <summary>
    /// Gets whether <c>pw-dump</c> is available.
    /// </summary>
    public static bool IsPwDumpAvailable => Registry.IsPwDumpAvailable;

    /// <summary>
    /// Gets whether <c>gdbus</c> is available.
    /// </summary>
    public static bool IsGdbusAvailable => Registry.IsGdbusAvailable;

    /// <summary>
    /// Gets whether the GStreamer <c>pipewiresrc</c> plugin is available.
    /// </summary>
    public static bool IsGstPipeWireSrcAvailable => Registry.IsGstPipeWireSrcAvailable;

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
