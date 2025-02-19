using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public abstract class WindowAccessor
{
    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public abstract ObservableCollection<WindowConfig> GetWindows();

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowPID">The PID of the window to raise</param>
    public abstract void RaiseWindow(string windowId);

    /// <summary>
    /// Take a screenshot of a window
    /// </summary>
    /// <param name="window">The window to screenshot</param>
    public abstract Bitmap? TakeScreenshot(string windowId);
}