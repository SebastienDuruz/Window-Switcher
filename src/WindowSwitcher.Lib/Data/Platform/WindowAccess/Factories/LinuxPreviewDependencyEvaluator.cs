using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

internal static class LinuxPreviewDependencyEvaluator
{
    public static bool SupportsPipeWire(
        ILinuxDependencyRegistry linuxDependencies,
        Func<bool>? availabilityProbe = null
    )
    {
        ArgumentNullException.ThrowIfNull(linuxDependencies);
        bool available = (availabilityProbe ?? LibPipeWireNative.IsAvailable)();
        return Ensure(linuxDependencies, LibPipeWireNative.LibraryName, available);
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
