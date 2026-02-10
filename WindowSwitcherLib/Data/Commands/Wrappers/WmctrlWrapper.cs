namespace WindowSwitcherLib.Data.Commands;

public class WmctrlWrapper() : CommandBase("wmctrl"), ICommandWrapper
{
    public string Execute(string args)
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }

}
