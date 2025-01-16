using Microsoft.VisualBasic.FileIO;

namespace WindowSwitcherLib.WindowAccess;

public static class StaticData
{
    public enum PrefixWindowType
    {
        whitelist,
        blacklist
    }
    public static string AppName { get; set; } = "WindowSwitcher";
    public static string DataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    public static string LinuxScreenshotFolder { get; set; } = Path.Combine(DataFolder, "LinuxScreenshot");
}