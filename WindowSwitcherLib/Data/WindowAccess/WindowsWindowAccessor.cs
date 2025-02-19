using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using static System.Drawing.Imaging.Encoder;
using static WindowSwitcherLib.Data.FileAccess.ConfigFileAccessor;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcherLib.Data.WindowAccess;

public class WindowsWindowAccessor : WindowAccessor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private static readonly ImageCodecInfo? JpegCodec =
        ImageCodecInfo.GetImageDecoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private static readonly EncoderParameters EncoderParameters = new(1)
    {
        Param =
        [
            new EncoderParameter(Quality, GetInstance().Config.ScreenshotQuality)
        ]
    };
    
    private ObservableCollection<WindowConfig> Windows { get; set; } = new();

    public override ObservableCollection<WindowConfig> GetWindows()
    {
        Windows.Clear();
        
        foreach (Process process in Process.GetProcesses())
        {
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                continue;
            if(process.MainWindowTitle.ToLower() == StaticData.AppName.ToLower())
                continue;
            if (process.HasExited)
                continue;

            Windows.Add(new WindowConfig()
            {
                WindowTitle = process.MainWindowTitle, 
                WindowId = process.MainWindowHandle.ToString(), 
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

    /// <summary>
    /// This method does not work for DirectX applications, this is why we use DWM thumnails instead
    /// </summary>
    /// <param name="windowId"></param>
    /// <returns></returns>
    public override Bitmap? TakeScreenshot(string windowId)
    {
        IntPtr hwnd = int.Parse(windowId);
        
        try
        {
            GetWindowRect(hwnd, out RECT rect);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
        
            using (System.Drawing.Bitmap bitmap = new(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(hwnd, hdc, 0);
                    g.ReleaseHdc(hdc);
                }
        
                string filePath = $"{DataFolders.ScreenshotFolder}/{windowId}.jpg";
                bitmap.Save(filePath, JpegCodec, EncoderParameters);
                return new Bitmap(filePath);
            }
        }
        catch (Exception ex)
        {
            return null;
        }
        finally
        {
            GC.Collect();
        }
    }
}