using System.IO.Compression;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class LinuxX11WindowAccessor : IWindowAccessor
{
    public List<Window> GetWindows()
    {
        string wmctrlOutput = WmctrlWrapper.ExecuteWmctrl(" -l");
        
        List<Window> windows = new List<Window>();
        string[] lines = wmctrlOutput.Split('\n');
        foreach (string line in lines)
            if(!String.IsNullOrWhiteSpace(line))
                windows.Add(new Window(){WindowId = line.Split(' ')[0], WindowTitle = this.ExtractWindowTitle(line)});
        
        return windows;
    }

    public void RaiseWindow(string windowPID)
    {
        WmctrlWrapper.ExecuteWmctrl($" -i -a \"{windowPID}\"");
    }

    private string ExtractWindowTitle(string windowInfo)
    {
        string windowTitle = "";

        try
        {
            string[] splitedWindowInfo = windowInfo.Split(' ');
            windowTitle = windowInfo.Substring(splitedWindowInfo[0].Length + splitedWindowInfo[1].Length + splitedWindowInfo[2].Length + splitedWindowInfo[3].Length + 4);
        }
        catch (Exception ex)
        {
            // TODO : Log
        }
        
        return windowTitle;
    }
}