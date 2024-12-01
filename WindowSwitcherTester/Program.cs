using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

IWindowAccessor accessor = WindowFactories.GetAccessor();

// Get the opened windows and raise the last one to front
List<Window> windows = accessor.GetWindows();
accessor.RaiseWindow(windows.First());
accessor.TakeScreenshot(windows.First());

