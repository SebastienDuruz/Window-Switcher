using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public class ShWrapper() : CommandBase("sh"), ICommandWrapper
{
    public string Execute(string args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return ExecuteWithArgumentList(["-lc", args], timeoutMs: 2_000);
    }
}
