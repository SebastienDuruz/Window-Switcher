using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public abstract class WindowAccessor
{
    protected ApplicationDataAccessor ApplicationDataAccessor = new();

    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public abstract List<Window> GetWindows();

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowPID">The PID of the window to raise</param>
    public abstract void RaiseWindow(Window window);

    /// <summary>
    /// Take a screenshot of a window
    /// </summary>
    /// <param name="window">The window to screenshot</param>
    public abstract void TakeScreenshot(Window window);
}