using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public sealed class WhichWrapper() : CommandBase("which"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }
}
