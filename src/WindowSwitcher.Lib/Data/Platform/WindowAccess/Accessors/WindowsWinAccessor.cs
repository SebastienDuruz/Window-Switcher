using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;
using WindowSwitcher.Lib.Data.Platform.Interop;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

[SupportedOSPlatform("windows")]
public class WindowsWinAccessor : WinAccessorBase
{
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int SwShow = 5;

    private static readonly ImageCodecInfo? JpegCodec = ImageCodecInfo
        .GetImageDecoders()
        .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private ObservableCollection<WindowConfig> Windows { get; set; } = new();

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        Windows.Clear();
        int currentProcessId = Process.GetCurrentProcess().Id;
        var processNameByPid = new Dictionary<uint, string>();

        _ = User32Functions.EnumWindows(
            (windowHandle, lParam) =>
            {
                try
                {
                    if (!IsEligibleTopLevelWindow(windowHandle))
                        return true;

                    string windowTitle = ReadWindowTitle(windowHandle);
                    if (string.IsNullOrWhiteSpace(windowTitle))
                        return true;
                    if (windowTitle.Equals(StaticData.AppName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    User32Functions.GetWindowThreadProcessId(windowHandle, out uint processId);
                    if (processId == 0 || processId == (uint)currentProcessId)
                        return true;

                    string processName = ResolveProcessName(processId, processNameByPid);
                    Windows.Add(
                        new WindowConfig
                        {
                            WindowTitle = windowTitle,
                            WindowId = windowHandle.ToString(),
                            ProcessName = processName,
                        }
                    );
                }
                catch
                {
                    // Ignore windows we cannot inspect.
                }

                return true;
            },
            IntPtr.Zero
        );

        return Windows;
    }

    private static bool IsEligibleTopLevelWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;
        if (!User32Functions.IsWindowVisible(windowHandle))
            return false;
        if (User32Functions.GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
            return false;
        if (User32Functions.GetWindowTextLength(windowHandle) <= 0)
            return false;

        return true;
    }

    private static string ReadWindowTitle(IntPtr windowHandle)
    {
        int titleLength = User32Functions.GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
            return string.Empty;

        var titleBuffer = new StringBuilder(titleLength + 1);
        int copiedLength = User32Functions.GetWindowText(
            windowHandle,
            titleBuffer,
            titleBuffer.Capacity
        );
        if (copiedLength <= 0)
            return string.Empty;

        return titleBuffer.ToString().Trim();
    }

    private static string ResolveProcessName(
        uint processId,
        IDictionary<uint, string> processNameByPid
    )
    {
        if (processNameByPid.TryGetValue(processId, out string? cachedName))
            return cachedName;

        string processName = string.Empty;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            // Keep empty process name if unavailable.
        }

        processNameByPid[processId] = processName;
        return processName;
    }

    public override void RaiseWindow(string windowId)
    {
        try
        {
            IntPtr windowHandle = IntPtr.Parse(windowId);
            BringWindowToFront(windowHandle);
        }
        catch (Exception) { }
    }
    
    public override Bitmap? TakeScreenshot(string windowId)
    {
        throw new NotImplementedException();
    }

    private static void BringWindowToFront(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        _ = User32Functions.ShowWindow(
            windowHandle,
            User32Functions.IsIconic(windowHandle) ? SwRestore : SwShow
        );

        IntPtr foregroundWindowHandle = User32Functions.GetForegroundWindow();
        uint currentThreadId = Kernel32Functions.GetCurrentThreadId();
        uint foregroundThreadId =
            foregroundWindowHandle == IntPtr.Zero
                ? 0
                : User32Functions.GetWindowThreadProcessId(foregroundWindowHandle, out _);
        uint targetThreadId = User32Functions.GetWindowThreadProcessId(windowHandle, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = User32Functions.AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    true
                );
            }

            if (
                targetThreadId != 0
                && targetThreadId != currentThreadId
                && targetThreadId != foregroundThreadId
            )
            {
                attachedToTarget = User32Functions.AttachThreadInput(
                    currentThreadId,
                    targetThreadId,
                    true
                );
            }

            _ = User32Functions.BringWindowToTop(windowHandle);
            _ = User32Functions.SetActiveWindow(windowHandle);
            _ = User32Functions.SetForegroundWindow(windowHandle);
            _ = User32Functions.ShowWindow(windowHandle, SwRestore);
        }
        finally
        {
            if (attachedToTarget)
                _ = User32Functions.AttachThreadInput(currentThreadId, targetThreadId, false);

            if (attachedToForeground)
                _ = User32Functions.AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    false
                );
        }
    }

    public override void RenameWindowTitle(string windowId, string windowTitle)
    {
        try
        {
            User32Functions.SetWindowText(IntPtr.Parse(windowId), windowTitle);
        }
        catch (Exception) { }
    }
}
