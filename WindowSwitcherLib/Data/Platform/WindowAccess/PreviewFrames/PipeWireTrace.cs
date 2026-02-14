using System.Text;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;

internal static class PipeWireTrace
{
    private const long MaxLogFileBytes = 2 * 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "window-switcher-pipewire.log");

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            lock (Sync)
            {
                TrimIfNeeded();
                string line = $"{DateTime.UtcNow:O} {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Best effort diagnostics.
        }
    }

    private static void TrimIfNeeded()
    {
        if (!File.Exists(LogPath))
            return;

        var info = new FileInfo(LogPath);
        if (info.Length <= MaxLogFileBytes)
            return;

        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
    }
}
