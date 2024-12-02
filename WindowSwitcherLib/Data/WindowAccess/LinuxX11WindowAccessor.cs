using System.IO.Compression;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class LinuxX11WindowAccessor : IWindowAccessor
{
    private WmctrlWrapper WmctrlWrapper { get; set; } = new();
    private ImportWrapper ImportWrapper { get; set; } = new();

    public string ScreenshotFolderPath { get; } = Path.Combine(Environment.SpecialFolder.ApplicationData.ToString(), "WindowSwitcher");

    public List<Window> GetWindows()
    {
        string wmctrlOutput = this.WmctrlWrapper.Execute(" -l");
        
        List<Window> windows = new List<Window>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
            if(!String.IsNullOrWhiteSpace(line))
                windows.Add(new Window(){WindowId = line.Split(' ')[0], WindowTitle = this.ExtractWindowTitle(line)});
        
        return windows;
    }

    public void RaiseWindow(Window window)
    {
        this.WmctrlWrapper.Execute($" -i -a \"{window.WindowId}\"");
    }

    public void TakeScreenshot(Window window)
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