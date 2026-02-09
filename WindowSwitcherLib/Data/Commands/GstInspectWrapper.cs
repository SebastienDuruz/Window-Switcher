namespace WindowSwitcherLib.Data.Commands;

public sealed class GstInspectWrapper() : CommandBase("gst-inspect-1.0"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_500);
    }
}
