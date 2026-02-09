using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.Interop;
using static System.Drawing.Imaging.Encoder;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcherLib.Data.WindowAccess;

public class WinsWinAccessor : WinAccessor
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
                ProcessName = process.ProcessName
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
        IntPtr hwnd = IntPtr.Parse(windowId);
        
        try
        {
            GetWindowRect(hwnd, out RECT rect);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            if (width <= 0 || height <= 0 || JpegCodec == null)
                return null;
        
            using (System.Drawing.Bitmap bitmap = new(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(hwnd, hdc, 0);
                    g.ReleaseHdc(hdc);
                }
        
                int quality = ConfigFileAccessor.GetInstance().ReadConfig(config => config.ScreenshotQuality);
                using var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(Quality, quality);
                using var stream = new MemoryStream();
                bitmap.Save(stream, JpegCodec, encoderParameters);
                stream.Position = 0;
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public override void RenameWindowTitle(string windowId, string windowTitle)
    {
        try
        {
            User32Functions.SetWindowText(IntPtr.Parse(windowId), windowTitle);
        }
        catch (Exception ex)
        {
            // TODO : Log
        }
    }
}
