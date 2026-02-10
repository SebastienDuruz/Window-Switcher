namespace WindowSwitcherLib.Data.Commands;

public static class LinuxDependencies
{
    private static readonly ILinuxDependencyRegistry Registry = new LinuxDependencyRegistry();

    public static event Action<string>? DependencyMissing
    {
        add => Registry.DependencyMissing += value;
        remove => Registry.DependencyMissing -= value;
    }

    public static bool IsWmctrlAvailable => Registry.IsWmctrlAvailable;
    public static bool IsImportAvailable => Registry.IsImportAvailable;
    public static bool IsGstLaunchAvailable => Registry.IsGstLaunchAvailable;
    public static bool IsPwDumpAvailable => Registry.IsPwDumpAvailable;
    public static bool IsGdbusAvailable => Registry.IsGdbusAvailable;
    public static bool IsGstPipeWireSrcAvailable => Registry.IsGstPipeWireSrcAvailable;

    public static IReadOnlyCollection<string> GetReportedMissing()
    {
        return Registry.GetReportedMissing();
    }

    public static void ReportMissingOnce(string dependency)
    {
        Registry.ReportMissingOnce(dependency);
    }
}
