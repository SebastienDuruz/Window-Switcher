using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcherLib.Data.WindowAccess;

public static class DataFolders
{
    public static string DataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StaticData.AppName);
    public static string ScreenshotFolder { get; set; } = Path.Combine(DataFolder, "Screenshots");
    public static string LogsFolder { get; set; } = Path.Combine(DataFolder, "Logs");

    public static void CheckFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ScreenshotFolder);
        Directory.CreateDirectory(LogsFolder);
    }
}