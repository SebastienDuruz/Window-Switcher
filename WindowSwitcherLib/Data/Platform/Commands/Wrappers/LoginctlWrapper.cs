using WindowSwitcherLib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class LoginctlWrapper() : CommandBase("loginctl"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }

    public string Execute(IReadOnlyList<string> args, int timeoutMs)
    {
        return ExecuteWithArgumentList(args, timeoutMs);
    }
}
