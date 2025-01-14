using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace WindowSwitcherLib.WindowAccess.CustomWindows;

public class EditListWindow : Window
{
    public List<string> ListToEdit { get; protected set; }
    
    public EditListWindow(List<string> listToEdit)
    {
        this.ListToEdit = listToEdit;
    }
}