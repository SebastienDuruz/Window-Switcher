using System.Collections.ObjectModel;
using System.IO.Compression;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class LinuxX11WindowAccessor : WindowAccessor
{
    private WmctrlWrapper WmctrlWrapper { get; set; } = new();
    private ImportWrapper ImportWrapper { get; set; } = new();
    
    public override ObservableCollection<WindowConfig> GetWindows()
    {
        string wmctrlOutput = this.WmctrlWrapper.Execute(" -l");
        
        ObservableCollection<WindowConfig> windows = new ObservableCollection<WindowConfig>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
            if (!String.IsNullOrWhiteSpace(line))
            {
                string windowName = this.ExtractWindowTitle(line);
                windows.Add(new WindowConfig()
                {
                    WindowId = line.Split(' ')[0], 
                    WindowTitle = windowName,
                    ShortWindowTitle = windowName.Length > 40 ? $"{windowName[..40]}..." : windowName
                });
            }
                
        return windows;
    }

    public override void RaiseWindow(WindowConfig window)
    {
        this.WmctrlWrapper.Execute($" -i -a \"{window.WindowId}\"");
    }

    public override void TakeScreenshot(WindowConfig window)
    {
        this.ImportWrapper.Execute(window.WindowTitle);
    }

    private string ExtractWindowTitle(string windowInfo)
    {
        string windowTitle = "";

        try
        {
            string[] splitedWindowInfo = windowInfo.Split(' ');
            windowTitle = windowInfo.Substring(splitedWindowInfo[0].Length + splitedWindowInfo[1].Length + splitedWindowInfo[2].Length + 3);
        }
        catch (Exception ex)
        {
            // TODO : Log
        }
        
        return windowTitle;
    }
}