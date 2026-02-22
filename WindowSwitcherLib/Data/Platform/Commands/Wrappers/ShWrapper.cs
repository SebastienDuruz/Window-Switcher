using WindowSwitcherLib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public class ShWrapper() : CommandBase("sh"), ICommandWrapper
{
    public string Execute(string args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return ExecuteWithArgumentList(["-lc", args], timeoutMs: 2_000);
    }
}
