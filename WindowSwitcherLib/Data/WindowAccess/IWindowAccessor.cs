using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public interface IWindowAccessor
{
    /// <summary>
    /// Get the list of Window currently opened
    /// </summary>
    /// <returns></returns>
    public List<Window> GetWindows();

    /// <summary>
    /// Raise the window to the front
    /// </summary>
    /// <param name="windowPID">The PID of the window to raise</param>
    public void RaiseWindow(string windowPID);
}