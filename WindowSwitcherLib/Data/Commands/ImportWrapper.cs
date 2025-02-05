using System.Diagnostics;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcherLib.WindowAccess;

public class ImportWrapper : ICommandWrapper
{
    public string Execute(string client)
    {
        using Process process = new Process();
        process.StartInfo.FileName = "import";
        process.StartInfo.Arguments = $"-window {client} -quality {ConfigFileAccessor.GetInstance().Config.ScreenshotQuality} {DataFolders.ScreenshotFolder}/{client}.jpg";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}