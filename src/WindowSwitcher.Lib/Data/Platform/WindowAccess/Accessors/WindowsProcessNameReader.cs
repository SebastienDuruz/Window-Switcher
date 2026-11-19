using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Serilog;
using WindowSwitcher.Lib.Data.Platform.Interop;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

/// <summary>
/// Reads Windows process names without taking a snapshot of every process on the system.
/// </summary>
internal static class WindowsProcessNameReader
{
    private const string ExecutableExtension = ".exe";

    // Long enough for extended-length paths (\\?\ prefix) without a retry loop.
    private const int ImagePathCapacity = 1024;

    private static readonly ILogger Logger = Log.ForContext(typeof(WindowsProcessNameReader));

    /// <summary>
    /// Reads the name of a process in the same format as <see cref="Process.ProcessName"/>.
    /// </summary>
    /// <returns>The process name, or an empty string when it cannot be read.</returns>
    [SupportedOSPlatform("windows")]
    public static string Read(uint processId)
    {
        if (processId == 0)
            return string.Empty;

        IntPtr processHandle = Kernel32Functions.OpenProcess(
            Kernel32Functions.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId
        );
        if (processHandle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            // Protected processes refuse even the limited access right: fall back to the
            // slower snapshot so their name is not lost. The cache keeps this rare.
            return error == Kernel32Functions.ERROR_ACCESS_DENIED
                ? ReadWithProcessSnapshot(processId)
                : string.Empty;
        }

        try
        {
            var imagePath = new StringBuilder(ImagePathCapacity);
            uint size = (uint)imagePath.Capacity;
            if (
                !Kernel32Functions.QueryFullProcessImageNameW(processHandle, 0, imagePath, ref size)
            )
            {
                Logger.Debug(
                    "Cannot query image name for PID {ProcessId}, error {Win32Error}",
                    processId,
                    Marshal.GetLastWin32Error()
                );
                return string.Empty;
            }

            return ProcessNameFromImagePath(imagePath.ToString(0, (int)size));
        }
        finally
        {
            _ = Kernel32Functions.CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// Converts a full image path into the format of <see cref="Process.ProcessName"/>:
    /// the file name, without a trailing <c>.exe</c> (case-insensitive) and nothing else removed.
    /// </summary>
    public static string ProcessNameFromImagePath(string imagePath)
    {
        ArgumentNullException.ThrowIfNull(imagePath);

        int separatorIndex = imagePath.LastIndexOfAny(['\\', '/']);
        string fileName = separatorIndex >= 0 ? imagePath[(separatorIndex + 1)..] : imagePath;
        return fileName.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ExecutableExtension.Length]
            : fileName;
    }

    private static string ReadWithProcessSnapshot(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The process may have exited between the two calls: keep an empty process name.
            Logger.Debug(exception, "Cannot resolve process name for PID {ProcessId}", processId);
            return string.Empty;
        }
    }
}
