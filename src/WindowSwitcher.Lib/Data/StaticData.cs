namespace WindowSwitcher.Lib.Data;

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

    /// <summary>
    /// Folder that holds the rolling application log files.
    /// Linux: <c>$XDG_STATE_HOME/WindowSwitcher/logs</c> (default <c>~/.local/state</c>).
    /// Windows: <c>%LOCALAPPDATA%\WindowSwitcher\logs</c>.
    /// </summary>
    public static string LogFolder { get; set; } =
        ResolveLogFolder(
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        );

    public static void CheckFolders()
    {
        Directory.CreateDirectory(DataFolder);
    }

    /// <summary>
    /// Resolves the log folder for the current platform.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable by name.</param>
    /// <param name="isWindows">Whether the current platform is Windows.</param>
    /// <param name="userHome">Absolute path of the user home directory.</param>
    /// <param name="localApplicationData">Absolute path of the local application data folder.</param>
    /// <returns>The absolute path of the log folder.</returns>
    public static string ResolveLogFolder(
        Func<string, string?> getEnvironmentVariable,
        bool isWindows,
        string userHome,
        string localApplicationData
    )
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(userHome);
        ArgumentNullException.ThrowIfNull(localApplicationData);

        if (isWindows)
            return Path.Combine(localApplicationData, AppName, "logs");

        // XDG Base Directory: relative paths are invalid and must be ignored.
        string? stateHome = getEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(stateHome) || !Path.IsPathRooted(stateHome))
            stateHome = Path.Combine(userHome, ".local", "state");

        return Path.Combine(stateHome, AppName, "logs");
    }
}
