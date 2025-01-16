using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

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
    
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    private const int SRCCOPY = 0x00CC0020; // Copier directement la source
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    public override ObservableCollection<WindowConfig?> GetWindows()
    {
        ObservableCollection<WindowConfig?> windows = new ObservableCollection<WindowConfig?>();
        
        foreach (Process process in Process.GetProcesses())
        {
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                continue;
            
            windows.Add(new WindowConfig()
            {
                WindowTitle = process.MainWindowTitle, 
                WindowId = process.MainWindowHandle.ToString(), 
                ShortWindowTitle = process.MainWindowTitle.Length > 40 ? $"{process.MainWindowTitle[..40]}..." : process.MainWindowTitle,
            });
        }
        
        return windows;
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

    public override Bitmap? TakeScreenshot(WindowConfig window)
    {
        IntPtr hwnd = IntPtr.Parse(window.WindowId);
        System.Drawing.Bitmap bmp;

        try
        {
            GetWindowRect(hwnd, out RECT rect);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            IntPtr hDC = GetWindowDC(hwnd);
            bmp = new System.Drawing.Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hDCGraphics = g.GetHdc();
                BitBlt(hDCGraphics, 0, 0, width, height, hDC, 0, 0, SRCCOPY);
                g.ReleaseHdc(hDCGraphics);
            }
            ReleaseDC(hwnd, hDC);
        }
        catch (Exception ex)
        {
            return null;
        }
        
        return ConvertToAvaloniaBitmap(bmp);
    }
    
    private Avalonia.Media.Imaging.Bitmap ConvertToAvaloniaBitmap(System.Drawing.Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        SaveBitmapWithLowerQuality(bitmap, memoryStream, ConfigFileAccessor.GetInstance().Config.ScreenshotQuality);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Avalonia.Media.Imaging.Bitmap(memoryStream);
    }
    
    private void SaveBitmapWithLowerQuality(System.Drawing.Bitmap bitmap, Stream outputStream, long quality)
    {
        var encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
        encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, quality);

        var jpegCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders()
            .FirstOrDefault(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

        if (jpegCodec != null)
        {
            bitmap.Save(outputStream, jpegCodec, encoderParameters);
        }
    }
}