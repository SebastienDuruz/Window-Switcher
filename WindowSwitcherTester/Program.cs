using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

IWindowAccessor accessor = WindowAccessorFactory.GetAccessor();

// Get the opened windows and raise the last one to front
List<Window> windows = accessor.GetWindows();
accessor.RaiseWindow(windows.Last().WindowId);

