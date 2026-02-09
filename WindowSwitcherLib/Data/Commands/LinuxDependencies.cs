using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data;

namespace WindowSwitcherLib.Data.Commands;

public static class LinuxDependencies
{
    private static readonly WhichWrapper Which = new();
    private static readonly GstInspectWrapper GstInspect = new();

    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ReportedMissing = new(StringComparer.OrdinalIgnoreCase);
    private static bool _wmctrlChecked;
    private static bool _wmctrlAvailable;
    private static bool _importChecked;
    private static bool _importAvailable;
    private static bool _gstLaunchChecked;
    private static bool _gstLaunchAvailable;
    private static bool _gstPipeWireSrcChecked;
    private static bool _gstPipeWireSrcAvailable;
    private static bool _pwDumpChecked;
    private static bool _pwDumpAvailable;
    private static bool _gdbusChecked;
    private static bool _gdbusAvailable;

    public static event Action<string>? DependencyMissing;

    public static bool IsWmctrlAvailable => CheckCached("wmctrl", ref _wmctrlChecked, ref _wmctrlAvailable);
    public static bool IsImportAvailable => CheckCached("import", ref _importChecked, ref _importAvailable);
    public static bool IsGstLaunchAvailable => CheckCached("gst-launch-1.0", ref _gstLaunchChecked, ref _gstLaunchAvailable);
    public static bool IsPwDumpAvailable => CheckCached("pw-dump", ref _pwDumpChecked, ref _pwDumpAvailable);
    public static bool IsGdbusAvailable => CheckCached("gdbus", ref _gdbusChecked, ref _gdbusAvailable);
    public static bool IsGstPipeWireSrcAvailable => CheckGstPipeWireSrcCached();

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

    private static bool CheckGstPipeWireSrcCached()
    {
        lock (SyncRoot)
        {
            if (!_gstPipeWireSrcChecked)
            {
                _gstPipeWireSrcAvailable = IsGstPipeWireSrcAvailableCore();
                _gstPipeWireSrcChecked = true;
            }

            return _gstPipeWireSrcAvailable;
        }
    }

    private static bool IsBinaryAvailable(string dependency)
    {
        string output = Which.Execute(dependency);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static bool IsGstPipeWireSrcAvailableCore()
    {
        if (!IsGstLaunchAvailable)
            return false;

        string output = GstInspect.Execute("pipewiresrc");
        return !string.IsNullOrWhiteSpace(output);
    }
}
