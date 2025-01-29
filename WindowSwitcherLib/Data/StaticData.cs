using Avalonia;
using Microsoft.VisualBasic.FileIO;

namespace WindowSwitcherLib.WindowAccess;

public static class StaticData
{
    public enum PrefixWindowType
    {
        whitelist,
        blacklist
    }

    /// <summary>
    /// Used to give the information to floating windows that the mainWindow is closing
    /// </summary>
    public static bool AppClosing { get; set; } = false;
    public static string AppName { get; set; } = "WindowSwitcher";
    public static string DataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    public static string LinuxScreenshotFolder { get; set; } = Path.Combine(DataFolder, "Screenshots");
    public static Thickness WindowListThickness { get; set; } = new Thickness(8, 2);
}