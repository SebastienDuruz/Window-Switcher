using System.Collections.ObjectModel;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Data.WindowAccess.Accessors;
using WindowSwitcherLib.Models;

WinAccessorBase accessorBase = WinFactories.GetAccessor();

// Get the opened windows and raise the last one to front
ObservableCollection<WindowConfig> windows = accessorBase.GetWindows();
accessorBase.RaiseWindow(windows[2].WindowId);
using var screenshot = accessorBase.TakeScreenshot(windows[2].WindowId);
