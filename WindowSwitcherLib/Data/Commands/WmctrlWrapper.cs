using System.Diagnostics;

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

        using Process process = CreateProcess();
        process.StartInfo.Arguments = args;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }

}
