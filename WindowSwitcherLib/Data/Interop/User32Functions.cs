using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.Interop;

public static class User32Functions
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);
}