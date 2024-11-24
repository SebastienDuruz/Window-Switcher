using System.Diagnostics;

namespace WindowSwitcherLib.WindowAccess;

public static class WmctrlWrapper
{
    public static string ExecuteWmctrl(string args)
    {
        using (Process process = new Process())
        {
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
    }
}