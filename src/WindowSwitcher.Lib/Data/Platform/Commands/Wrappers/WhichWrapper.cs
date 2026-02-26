using WindowSwitcherLib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class WhichWrapper() : CommandBase("which"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }
}
