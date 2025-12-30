WindowAccessor accessor = WindowFactories.GetAccessor();

// Get the opened windows and raise the last one to front
ObservableCollection<WindowConfig> windows = accessor.GetWindows();
accessor.RaiseWindow(windows[2].WindowId);
accessor.TakeScreenshot(windows[2].WindowId);

