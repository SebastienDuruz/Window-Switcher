using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcherLib.Data.Commands;

public class ImportWrapper() : CommandBase("import"), ICommandWrapper
{
    public string Execute(string client)
    {
        _process.StartInfo.Arguments = $"-window {client} -quality {ConfigFileAccessor.GetInstance().Config.ScreenshotQuality} {DataFolders.ScreenshotFolder}/{client}.jpg";

        _process.Start();
        string output = _process.StandardOutput.ReadToEnd();
        _process.WaitForExit();

        return output;
    }
}