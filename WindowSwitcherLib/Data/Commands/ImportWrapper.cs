using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcherLib.Data.Commands;

public class ImportWrapper() : CommandBase("import"), ICommandWrapper
{
    public string Execute(string client)
    {
        using var process = CreateProcess();
        int quality = ConfigFileAccessor.GetInstance().ReadConfig(config => config.ScreenshotQuality);
        process.StartInfo.Arguments = $"-window {client} -quality {quality} {DataFolders.ScreenshotFolder}/{client}.jpg";

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}
