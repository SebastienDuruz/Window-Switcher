using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowSwitcherLib.Data.Common;
using WindowSwitcherLib.Data.Configuration;
using WindowSwitcherLib.Data.Logging;
using WindowSwitcherLib.Data.Platform.Interop;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Domain.Models;
using static System.Drawing.Imaging.Encoder;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Accessors;

public class WindowsWinAccessor : WinAccessorBase
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
            try
            {
                if (process.HasExited || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    continue;
                if (process.MainWindowTitle.Equals(StaticData.AppName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Windows.Add(new WindowConfig
                {
                    WindowTitle = process.MainWindowTitle,
                    WindowId = process.MainWindowHandle.ToString(),
                    ProcessName = process.ProcessName
                });
            }
            catch
            {
                // Ignore processes we cannot inspect.
            }
            finally
            {
                process.Dispose();
            }
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
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"RaiseWindow failed for {windowId}: {ex.Message}", StaticData.LogSeverity.WARN);
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

                int quality = 80;
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
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"Windows screenshot failed for {windowId}: {ex.Message}", StaticData.LogSeverity.WARN);
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
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"RenameWindowTitle failed for {windowId}: {ex.Message}", StaticData.LogSeverity.WARN);
        }
    }
}
