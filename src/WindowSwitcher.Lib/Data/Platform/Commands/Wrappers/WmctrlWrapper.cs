using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public class WmctrlWrapper() : CommandBase("wmctrl"), ICommandWrapper
{
    public string Execute(string args)
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }

    public string Execute(IReadOnlyList<string> args, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return ExecuteWithArgumentList(args, timeoutMs);
    }
}
