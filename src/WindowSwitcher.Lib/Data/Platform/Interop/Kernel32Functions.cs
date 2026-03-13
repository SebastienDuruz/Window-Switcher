using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.Interop;

internal static class Kernel32Functions
{
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
