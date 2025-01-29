using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using static System.Drawing.Graphics;
using static System.Drawing.Imaging.Encoder;
using static WindowSwitcherLib.Data.FileAccess.ConfigFileAccessor;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcherLib.Data.WindowAccess;

public class WindowsWindowAccessor : WindowAccessor
{
    private const int SRCCOPY = 0x00CC0020;
    
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    private static readonly ImageCodecInfo? PngCodec =
        ImageCodecInfo.GetImageDecoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Png.Guid);

    private static readonly EncoderParameters EncoderParameters = new(1)
    {
        Param =
        [
            new System.Drawing.Imaging.EncoderParameter(Quality,
                GetInstance().Config.ScreenshotQuality) // Valeur par défaut
        ]
    };
    
    private ObservableCollection<WindowConfig> Windows { get; set; } = new();

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        foreach (Process process in Process.GetProcesses())
        {
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                continue;
            if(process.MainWindowTitle.ToLower() == StaticData.AppName.ToLower())
                continue;

            if (Windows.Any(x => x.WindowTitle == process.MainWindowTitle))
                Windows.Remove(Windows.FirstOrDefault(x => x.WindowTitle == process.MainWindowTitle)!);
            
            Windows.Add(new WindowConfig()
            {
                WindowTitle = process.MainWindowTitle, 
                WindowId = process.MainWindowHandle.ToString(), 
                ShortWindowTitle = process.MainWindowTitle.Length > 40 ? $"{process.MainWindowTitle[..40]}..." : process.MainWindowTitle,
            });    
        }
        
        return Windows;
    }

    public override void RaiseWindow(string windowId)
    {
        try
        {
            SetForegroundWindow(IntPtr.Parse(windowId));
        }
        catch (Exception ex)
        {
            // TODO : Log            
        }
    }

    public override Bitmap? TakeScreenshot(string windowId)
    {
        IntPtr hwnd = IntPtr.Parse(windowId);

        try
        {
            GetWindowRect(hwnd, out RECT rect);
            IntPtr hDc = GetWindowDC(hwnd);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (var bitmap = new System.Drawing.Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        IntPtr hDcGraphics = g.GetHdc();
                        BitBlt(hDcGraphics, 0, 0, width, height, hDc, 0, 0, SRCCOPY);
                        g.ReleaseHdc(hDcGraphics);
                    }

                    bitmap.Save(memoryStream, PngCodec, EncoderParameters);
                }

                ReleaseDC(hwnd, hDc);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return new Bitmap(memoryStream);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }
    }
}