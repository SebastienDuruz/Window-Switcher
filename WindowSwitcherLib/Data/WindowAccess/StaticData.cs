using Microsoft.VisualBasic.FileIO;

namespace WindowSwitcherLib.WindowAccess;

public static class StaticData
{
    public static string ScreenshotFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowSwitcher");
}