using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public interface IWindowAccessor
{
    /// <summary>
    /// The Folder that contains the screenshots
    /// </summary>
    public string ScreenshotFolderPath { get; }
    
    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public List<Window> GetWindows();

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowPID">The PID of the window to raise</param>
    public void RaiseWindow(Window window);
    
    /// <summary>
    /// Take a screenshot of a window
    /// </summary>
    /// <param name="window">The window to screenshot</param>
    public void TakeScreenshot(Window window);
}