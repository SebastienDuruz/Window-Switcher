using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public sealed class WhichWrapper() : CommandBase("which"), ICommandWrapper
{
    public string Execute(string args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(args);
        return ExecuteWithArgumentList([args], timeoutMs: 2_000);
    }
}
