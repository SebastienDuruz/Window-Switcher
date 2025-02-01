using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcherLib.Data.WindowAccess;

public class LinuxX11WindowAccessor : WindowAccessor
{
    private WmctrlWrapper WmctrlWrapper { get; set; } = new();
    private ImportWrapper ImportWrapper { get; set; } = new();
    
    public override ObservableCollection<WindowConfig> GetWindows()
    {
        string wmctrlOutput = WmctrlWrapper.Execute(" -l");
        
        ObservableCollection<WindowConfig> windows = new ObservableCollection<WindowConfig>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
            if (!String.IsNullOrWhiteSpace(line))
            {
                string windowName = ExtractWindowTitle(line);
                windows.Add(new WindowConfig()
                {
                    WindowId = line.Split(' ')[0], 
                    WindowTitle = windowName,
                    ShortWindowTitle = windowName.Length > 40 ? $"{windowName[..40]}..." : windowName
                });
            }
                
        return windows;
    }

    public override void RaiseWindow(string windowId)
    {
        WmctrlWrapper.Execute($" -i -a \"{windowId}\"");
    }

    public override Bitmap? TakeScreenshot(string windowId)
    {
        string commandOutput = ImportWrapper.Execute(windowId);

        if (commandOutput == "")
        {
            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes($"{StaticData.ScreenshotFolder}/{windowId}.jpg")))
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                // Todo : Log
            }
        }

        return null;
    }

    private string ExtractWindowTitle(string windowInfo)
    {
        string windowTitle = "";

        try
        {
            windowInfo = windowInfo.Trim().Replace("  ", " ");
            string[] splitedWindowInfo = windowInfo.Split(' ');
            windowTitle = windowInfo.Substring(splitedWindowInfo[0].Length + splitedWindowInfo[1].Length + splitedWindowInfo[2].Length + 3);
        }
        catch (Exception ex)
        {
            // TODO : Log
            Console.WriteLine(ex.Message);
        }
        
        return windowTitle;
    }
}