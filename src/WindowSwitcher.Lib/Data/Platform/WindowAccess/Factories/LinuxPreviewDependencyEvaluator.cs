using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

internal static class LinuxPreviewDependencyEvaluator
{
    public static bool SupportsPipeWire(ILinuxDependencyRegistry linuxDependencies)
    {
        ArgumentNullException.ThrowIfNull(linuxDependencies);

        return Ensure(linuxDependencies, "gst-launch-1.0", linuxDependencies.IsGstLaunchAvailable)
            && Ensure(
                linuxDependencies,
                "gstreamer-pipewire",
                linuxDependencies.IsGstPipeWireSrcAvailable
            )
            && Ensure(linuxDependencies, "pw-dump", linuxDependencies.IsPwDumpAvailable);
    }

    private static bool Ensure(
        ILinuxDependencyRegistry linuxDependencies,
        string dependencyName,
        bool isAvailable
    )
    {
        if (isAvailable)
            return true;

        linuxDependencies.ReportMissingOnce(dependencyName);
        return false;
    }
}
