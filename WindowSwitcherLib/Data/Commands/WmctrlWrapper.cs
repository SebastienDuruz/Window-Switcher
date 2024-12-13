using System.Diagnostics;

namespace WindowSwitcherLib.WindowAccess;

public class WmctrlWrapper : ICommandWrapper
{
    public string Execute(string args)
    {
        if(!IsWmctrlInstalled()) 
            throw new ApplicationException("Wmctrl is not installed.");
        
        using Process process = new Process();
        process.StartInfo.FileName = "wmctrl";
        process.StartInfo.Arguments = args;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }

    private bool IsWmctrlInstalled()
    {
        Process process = new Process
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