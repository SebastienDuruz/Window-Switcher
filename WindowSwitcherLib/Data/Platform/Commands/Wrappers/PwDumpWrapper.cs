using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class PwDumpWrapper() : CommandBase("pw-dump"), IPwDumpWrapper
{
    public string Execute(string args)
    {
        if (LinuxDependencies.IsPwDumpAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("pw-dump");
        return string.Empty;
    }

    public string Execute(int timeoutMs)
    {
        if (LinuxDependencies.IsPwDumpAvailable)
            return ExecuteWithArguments(string.Empty, timeoutMs);
        LinuxDependencies.ReportMissingOnce("pw-dump");
        return string.Empty;
    }
}
