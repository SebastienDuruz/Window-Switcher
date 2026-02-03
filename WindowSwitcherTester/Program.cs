using System.Collections.ObjectModel;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;

WindowAccessor accessor = WindowFactories.GetAccessor();

// Get the opened windows and raise the last one to front
ObservableCollection<WindowConfig> windows = accessor.GetWindows();
accessor.RaiseWindow(windows[2].WindowId);
using var screenshot = accessor.TakeScreenshot(windows[2].WindowId);
