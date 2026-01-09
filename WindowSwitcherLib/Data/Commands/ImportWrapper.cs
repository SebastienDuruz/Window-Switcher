using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcherLib.Data.Commands;

public class ImportWrapper() : CommandBase("import"), ICommandWrapper
{
    public string Execute(string client)
    {
        using var process = CreateProcess();
        process.StartInfo.Arguments = $"-window {client} -quality {ConfigFileAccessor.GetInstance().Config.ScreenshotQuality} {DataFolders.ScreenshotFolder}/{client}.jpg";

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}
