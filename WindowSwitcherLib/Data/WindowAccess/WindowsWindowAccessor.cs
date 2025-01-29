using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using static System.Drawing.Graphics;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcherLib.Data.WindowAccess;

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
    
    private System.Drawing.Bitmap Bmp { get; set; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
    
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    private const int SRCCOPY = 0x00CC0020;
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    private static readonly System.Drawing.Imaging.ImageCodecInfo JpegCodec =
        System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders()
            .FirstOrDefault(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

    private static readonly System.Drawing.Imaging.EncoderParameters EncoderParameters = new (1)
    {
        Param = new[]
        {
            new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, ConfigFileAccessor.GetInstance().Config.ScreenshotQuality) // Valeur par défaut
        }
    };
    
    private ObservableCollection<WindowConfig?> Windows { get; set; } = new();

    public override ObservableCollection<WindowConfig?> GetWindows()
    {
        foreach (Process process in Process.GetProcesses())
        {
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                continue;
            if(process.MainWindowTitle.ToLower() == StaticData.AppName.ToLower())
                continue;

            if (Windows.Any(x => x.WindowTitle == process.MainWindowTitle))
                Windows.Remove(Windows.FirstOrDefault(x => x.WindowTitle == process.MainWindowTitle));
            
            Windows.Add(new WindowConfig()
            {
                WindowTitle = process.MainWindowTitle, 
                WindowId = process.MainWindowHandle.ToString(), 
                ShortWindowTitle = process.MainWindowTitle.Length > 40 ? $"{process.MainWindowTitle[..40]}..." : process.MainWindowTitle,
            });    
        }
        
        return Windows;
    }

    public override void RaiseWindow(WindowConfig? window)
    {
        try
        {
            SetForegroundWindow(IntPtr.Parse(window.WindowId));
        }
        catch (Exception ex)
        {
            // TODO : Log            
        }
    }

    public override Bitmap? TakeScreenshot(WindowConfig? window)
    {
        IntPtr hwnd = IntPtr.Parse(window.WindowId);

        try
        {
            GetWindowRect(hwnd, out RECT rect);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            IntPtr hDc = GetWindowDC(hwnd);
            Bmp = new System.Drawing.Bitmap(width, height);
            using (Graphics g = FromImage(Bmp))
            {
                IntPtr hDcGraphics = g.GetHdc();
                BitBlt(hDcGraphics, 0, 0, width, height, hDc, 0, 0, SRCCOPY);
                g.ReleaseHdc(hDcGraphics);
            }
            ReleaseDC(hwnd, hDc);
        }
        catch (Exception ex)
        {
            return null;
        }
        
        using var memoryStream = new MemoryStream();
        Bmp.Save(memoryStream, JpegCodec, EncoderParameters);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }
}