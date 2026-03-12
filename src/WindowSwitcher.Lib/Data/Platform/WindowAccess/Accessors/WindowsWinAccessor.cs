using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowSwitcher.Lib.Data.Platform.Interop;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using static System.Drawing.Imaging.Encoder;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

[SupportedOSPlatform("windows")]
public class WindowsWinAccessor : WinAccessorBase
{
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int SwShow = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint idAttach,
        uint idAttachTo,
        [MarshalAs(UnmanagedType.Bool)] bool fAttach
    );

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder titleBuffer,
        int maxCount
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static readonly ImageCodecInfo? JpegCodec = ImageCodecInfo
        .GetImageDecoders()
        .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private ObservableCollection<WindowConfig> Windows { get; set; } = new();

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        Windows.Clear();
        int currentProcessId = Process.GetCurrentProcess().Id;
        var processNameByPid = new Dictionary<uint, string>();

        _ = EnumWindows(
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

                    GetWindowThreadProcessId(windowHandle, out uint processId);
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
        if (!IsWindowVisible(windowHandle))
            return false;
        if (GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
            return false;
        if (GetWindowTextLength(windowHandle) <= 0)
            return false;

        return true;
    }

    private static string ReadWindowTitle(IntPtr windowHandle)
    {
        int titleLength = GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
            return string.Empty;

        var titleBuffer = new StringBuilder(titleLength + 1);
        int copiedLength = GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
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

        _ = ShowWindow(windowHandle, IsIconic(windowHandle) ? SwRestore : SwShow);

        IntPtr foregroundWindowHandle = GetForegroundWindow();
        uint currentThreadId = GetCurrentThreadId();
        uint foregroundThreadId =
            foregroundWindowHandle == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(foregroundWindowHandle, out _);
        uint targetThreadId = GetWindowThreadProcessId(windowHandle, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(
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
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            _ = BringWindowToTop(windowHandle);
            _ = SetActiveWindow(windowHandle);
            _ = SetForegroundWindow(windowHandle);
            _ = ShowWindow(windowHandle, SwRestore);
        }
        finally
        {
            if (attachedToTarget)
                _ = AttachThreadInput(currentThreadId, targetThreadId, false);

            if (attachedToForeground)
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
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
