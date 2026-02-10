using WindowSwitcherLib.Data.Common;

namespace WindowSwitcherLib.Data.Logging;

public static class AppLogger
{
    private static readonly SemaphoreSlim LogLock = new(1, 1);
    public static string LogFilePath => Path.Combine(StaticData.LogsFolder, $"{DateTime.Now:yyyy-MM-dd}.txt");
    public static string LastLogMessage { get; private set; } = string.Empty;

    public static void Log(string message, StaticData.LogSeverity severity)
    {
        string logMessage = $"\n[{severity}] [{DateTime.Now:O}] {message}";
        LastLogMessage = logMessage;
        _ = WriteLogAsync(logMessage);
    }

    private static async Task WriteLogAsync(string message)
    {
        bool lockTaken = false;
        try
        {
            StaticData.CheckFolders();
            await LogLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            await File.AppendAllTextAsync(LogFilePath, message).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort logging: never block or crash the app on log failures.
        }
        finally
        {
            if (lockTaken)
                LogLock.Release();
        }
    }
}
