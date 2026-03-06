using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

public sealed class LinuxDependencyRegistry(
    ICommandWrapper? whichWrapper = null,
    ICommandWrapper? gstInspectWrapper = null
) : ILinuxDependencyRegistry
{
    private const string WmctrlBinary = "wmctrl";
    private const string ImportBinary = "import";
    private const string GstLaunchBinary = "gst-launch-1.0";
    private const string PwDumpBinary = "pw-dump";
    private const string GdbusBinary = "gdbus";
    private const string GstPipeWirePlugin = "pipewiresrc";

    private readonly object _syncRoot = new();
    private readonly HashSet<string> _reportedMissing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _binaryAvailabilityCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ICommandWrapper _whichWrapper = whichWrapper ?? new WhichWrapper();
    private readonly ICommandWrapper _gstInspectWrapper =
        gstInspectWrapper ?? new GstInspectWrapper();

    private bool _gstPipeWireSrcChecked;
    private bool _gstPipeWireSrcAvailable;

    public event Action<string>? DependencyMissing;

    public bool IsWmctrlAvailable => CheckBinaryCached(WmctrlBinary);
    public bool IsImportAvailable => CheckBinaryCached(ImportBinary);
    public bool IsGstLaunchAvailable => CheckBinaryCached(GstLaunchBinary);
    public bool IsPwDumpAvailable => CheckBinaryCached(PwDumpBinary);
    public bool IsGdbusAvailable => CheckBinaryCached(GdbusBinary);
    public bool IsGstPipeWireSrcAvailable => CheckGstPipeWireSrcCached();

    public IReadOnlyCollection<string> GetReportedMissing()
    {
        lock (_syncRoot)
        {
            return _reportedMissing.ToArray();
        }
    }

    public void ReportMissingOnce(string dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency))
            return;

        bool shouldReport;
        lock (_syncRoot)
        {
            shouldReport = _reportedMissing.Add(dependency);
        }

        if (!shouldReport)
            return;

        DependencyMissing?.Invoke(dependency);
    }

    private bool CheckBinaryCached(string dependency)
    {
        lock (_syncRoot)
        {
            if (_binaryAvailabilityCache.TryGetValue(dependency, out bool available))
                return available;

            available = IsBinaryAvailable(dependency);
            _binaryAvailabilityCache[dependency] = available;
            return available;
        }
    }

    private bool CheckGstPipeWireSrcCached()
    {
        lock (_syncRoot)
        {
            if (_gstPipeWireSrcChecked)
                return _gstPipeWireSrcAvailable;

            _gstPipeWireSrcAvailable = IsGstPipeWireSrcAvailableCore();
            _gstPipeWireSrcChecked = true;
            return _gstPipeWireSrcAvailable;
        }
    }

    private bool IsBinaryAvailable(string dependency)
    {
        string output = _whichWrapper.Execute(dependency);
        return !string.IsNullOrWhiteSpace(output);
    }

    private bool IsGstPipeWireSrcAvailableCore()
    {
        if (!IsGstLaunchAvailable)
            return false;

        string output = _gstInspectWrapper.Execute(GstPipeWirePlugin);
        return !string.IsNullOrWhiteSpace(output);
    }
}
