using System.Diagnostics;

namespace WindowSwitcherLib.WindowAccess;

public class ImportWrapper : ICommandWrapper
{
    public string Execute(string client)
    {
        using Process process = new Process();
        process.StartInfo.FileName = "import";
        process.StartInfo.Arguments = $"-window \"{client}\" -quality 25 /home/smour/screenshot.jpg";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}