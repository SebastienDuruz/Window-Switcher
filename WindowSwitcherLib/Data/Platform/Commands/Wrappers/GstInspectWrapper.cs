using WindowSwitcherLib.Data.Platform.Commands.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class GstInspectWrapper() : CommandBase("gst-inspect-1.0"), ICommandWrapper
{
    public string Execute(string args)
    {
        return ExecuteWithArguments(args, timeoutMs: 2_500);
    }
}
