namespace WindowSwitcherLib.Data;

public static class StaticData
{
    public enum PrefixWindowType
    {
        whitelist,
        blacklist,
    }

    /// <summary>
    /// Used to give the information to floating windows that the mainWindow is closing
    /// </summary>
    public static bool AppClosing { get; set; } = false;

    public static string AppName { get; set; } = "WindowSwitcher";

    public static string DataFolder { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            StaticData.AppName
        );

    public static void CheckFolders()
    {
        Directory.CreateDirectory(DataFolder);
    }
}
