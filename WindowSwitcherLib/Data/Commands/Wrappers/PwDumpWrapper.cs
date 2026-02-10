namespace WindowSwitcherLib.Data.Commands;

public sealed class PwDumpWrapper() : CommandBase("pw-dump"), IPwDumpWrapper
{
    public string Execute(string args)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
        {
            LinuxDependencies.ReportMissingOnce("pw-dump");
            return string.Empty;
        }

        return ExecuteWithArguments(args, timeoutMs: 2_500);
    }

    public string Execute(int timeoutMs)
    {
        if (!LinuxDependencies.IsPwDumpAvailable)
        {
            LinuxDependencies.ReportMissingOnce("pw-dump");
            return string.Empty;
        }

        return ExecuteWithArguments(string.Empty, timeoutMs);
    }
}
