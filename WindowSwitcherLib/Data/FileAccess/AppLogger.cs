using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcherLib.Data.FileAccess;

public static class AppLogger
{
    public static string LogFilePath { get; set; } =
        Path.Combine(DataFolders.LogsFolder, $"{DateTime.Today.Date.ToLongDateString()}.txt");
    public static string LastLogMessage { get; set; } = string.Empty;
    public static async void Log(string message, StaticData.LogSeverity severity)
    {
        LastLogMessage = $"\n[{severity}] [{DateTime.Now}] {message}";
        if(ConfigFileAccessor.GetInstance().Config.ActivateLogs)
            await File.AppendAllTextAsync(LogFilePath, LastLogMessage);
    }
}