using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.WindowAccess;

public class MacOsWindowAccessor : WindowAccessor
{
    public override ObservableCollection<WindowConfig> GetWindows()
    {
        throw new NotImplementedException();
    }

    public override void RaiseWindow(string windowId)
    {
        throw new NotImplementedException();
    }

    public override Bitmap? TakeScreenshot(string windowId)
    {
        throw new NotImplementedException();
    }
}