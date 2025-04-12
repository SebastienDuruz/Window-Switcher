using System.Runtime.InteropServices;
using System.Text;

namespace WindowSwitcherLib.Data.Interop;

public static class User32Functions
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);
    
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static void HideFromAltTab(IntPtr hWnd)
    {
        IntPtr exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        IntPtr newStyle = new IntPtr((exStyle.ToInt64() & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, newStyle);
    }
}