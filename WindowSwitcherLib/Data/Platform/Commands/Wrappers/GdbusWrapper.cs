using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class GdbusWrapper() : CommandBase("gdbus"), IGdbusWrapper
{
    public string Execute(string args)
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }

    public string Execute(IReadOnlyList<string> args, int timeoutMs)
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return ExecuteWithArgumentList(args, timeoutMs);
        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }
}
