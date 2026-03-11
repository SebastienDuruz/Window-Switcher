using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

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
            return ExecuteWithArgumentList([], timeoutMs);
        LinuxDependencies.ReportMissingOnce("pw-dump");
        return string.Empty;
    }
}
