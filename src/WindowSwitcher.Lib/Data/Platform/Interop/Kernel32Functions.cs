using System.Runtime.InteropServices;
using System.Text;

namespace WindowSwitcher.Lib.Data.Platform.Interop;

internal static class Kernel32Functions
{
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const int ERROR_ACCESS_DENIED = 5;

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr process,
        uint flags,
        StringBuilder exeName,
        ref uint size
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
