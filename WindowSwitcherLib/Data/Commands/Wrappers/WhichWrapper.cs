namespace WindowSwitcherLib.Data.Commands;

public sealed class WhichWrapper() : CommandBase("which"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }
}
