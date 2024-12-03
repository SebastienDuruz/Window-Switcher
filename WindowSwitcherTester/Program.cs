using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

WindowAccessor accessor = WindowFactories.GetAccessor();

// Get the opened windows and raise the last one to front
List<Window> windows = accessor.GetWindows();
accessor.RaiseWindow(windows[2]);
accessor.TakeScreenshot(windows[2]);

