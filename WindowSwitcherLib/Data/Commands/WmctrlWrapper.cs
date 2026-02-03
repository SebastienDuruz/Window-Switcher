using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public class WmctrlWrapper() : CommandBase("wmctrl"), ICommandWrapper
{
    public string Execute(string args)
    {
        if(!IsWmctrlInstalled()) 
            throw new ApplicationException("Wmctrl is not installed.");

        using Process process = CreateProcess();
        process.StartInfo.Arguments = args;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }

    private bool IsWmctrlInstalled()
    {
        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "wmctrl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        
        return !string.IsNullOrEmpty(output);
    }
}
