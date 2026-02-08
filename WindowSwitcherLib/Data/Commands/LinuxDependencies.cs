using System.Diagnostics;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data;

namespace WindowSwitcherLib.Data.Commands;

public static class LinuxDependencies
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ReportedMissing = new(StringComparer.OrdinalIgnoreCase);
    private static bool _wmctrlChecked;
    private static bool _wmctrlAvailable;
    private static bool _importChecked;
    private static bool _importAvailable;
    private static bool _gdbusChecked;
    private static bool _gdbusAvailable;
    private static bool _gstLaunchChecked;
    private static bool _gstLaunchAvailable;

    public static event Action<string>? DependencyMissing;

    public static bool IsWmctrlAvailable => CheckCached("wmctrl", ref _wmctrlChecked, ref _wmctrlAvailable);
    public static bool IsImportAvailable => CheckCached("import", ref _importChecked, ref _importAvailable);
    public static bool IsGdbusAvailable => CheckCached("gdbus", ref _gdbusChecked, ref _gdbusAvailable);
    public static bool IsGstLaunchAvailable => CheckCached("gst-launch-1.0", ref _gstLaunchChecked, ref _gstLaunchAvailable);

    public static IReadOnlyCollection<string> GetReportedMissing()
    {
        lock (SyncRoot)
        {
            return ReportedMissing.ToList();
        }
    }

    public static void ReportMissingOnce(string dependency)
    {
        bool shouldReport;
        lock (SyncRoot)
        {
            shouldReport = ReportedMissing.Add(dependency);
        }

        if (!shouldReport)
            return;

        AppLogger.Log($"Missing dependency: {dependency}", StaticData.LogSeverity.WARN);
        DependencyMissing?.Invoke(dependency);
    }

    private static bool CheckCached(string dependency, ref bool checkedFlag, ref bool available)
    {
        lock (SyncRoot)
        {
            if (!checkedFlag)
            {
                available = IsBinaryAvailable(dependency);
                checkedFlag = true;
            }

            return available;
        }
    }

    private static bool IsBinaryAvailable(string dependency)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = dependency,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }
}
