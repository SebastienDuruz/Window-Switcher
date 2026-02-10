using WindowSwitcherLib.Data.Configuration;
using WindowSwitcherLib.Data.Logging;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Data.Common;

public static class StaticData
{
    public enum PrefixWindowType
    {
        whitelist,
        blacklist
    }

    public enum LogSeverity
    {
        INFO,
        WARN,
        ERRO,
        CRIT
    }

    /// <summary>
    /// Used to give the information to floating windows that the mainWindow is closing
    /// </summary>
    public static bool AppClosing { get; set; } = false;

    public static string AppName { get; set; } = "WindowSwitcher";
    
    public static string DataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StaticData.AppName);
    public static string LogsFolder { get; set; } = Path.Combine(DataFolder, "Logs");

    public static void CheckFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(LogsFolder);
    }
    
    public static bool EnsureLinuxDependency(string dependencyName, bool isAvailable, string logWhenMissing)
    {
        if (isAvailable)
            return true;

        LinuxDependencies.ReportMissingOnce(dependencyName);
        LogIfEnabled(logWhenMissing);
        return false;
    }

    public static void LogIfEnabled(string message)
    {
        if (!ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
            return;

        AppLogger.Log($"[PreviewProviderFactory] {message}", StaticData.LogSeverity.WARN);
    }
}