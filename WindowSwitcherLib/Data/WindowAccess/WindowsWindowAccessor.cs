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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public override List<Window> GetWindows()
    {
        List<Window> windows = new List<Window>();
        
        foreach (Process process in Process.GetProcesses())
        {
            if (process.MainWindowHandle == IntPtr.Zero)
                continue;
            windows.Add(new Window(){WindowTitle = process.MainWindowTitle, WindowId = process.MainWindowHandle.ToString()});
        }
        
        return windows;
    }

    public override void RaiseWindow(Window window)
    {
        SetForegroundWindow(IntPtr.Parse(window.WindowId));
    }

    public override void TakeScreenshot(Window window)
    {
        RECT rect;
        GetWindowRect(IntPtr.Parse(window.WindowId), out rect);
        
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        using (Bitmap bitmap = new Bitmap(width, height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            }
            bitmap.Save(Path.Combine(ApplicationDataAccessor.ScreenshotFolder, $"screenshot_{window.WindowId}.jpeg"), ImageFormat.Jpeg);
        }
    }
}