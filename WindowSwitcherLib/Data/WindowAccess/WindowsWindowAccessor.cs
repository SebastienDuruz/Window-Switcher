using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class WindowsWindowAccessor : WindowAccessor
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        ObservableCollection<WindowConfig> windows = new ObservableCollection<WindowConfig>();
        
        foreach (Process process in Process.GetProcesses())
        {
            if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle) || !IsWindowVisible(process.MainWindowHandle))
                continue;
            
            windows.Add(new WindowConfig()
            {
                WindowTitle = process.MainWindowTitle, 
                WindowId = process.MainWindowHandle.ToString(), 
                ShortWindowTitle = process.MainWindowTitle.Length > 30 ? $"{process.MainWindowTitle[..30]}..." : process.MainWindowTitle,
            });
        }
        
        return windows;
    }

    public override void RaiseWindow(WindowConfig window)
    {
        SetForegroundWindow(IntPtr.Parse(window.WindowId));
    }

    public override void TakeScreenshot(WindowConfig window)
    {
        throw new NotImplementedException();
    }
}