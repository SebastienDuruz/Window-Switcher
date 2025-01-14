using Avalonia.Controls;

namespace WindowSwitcherLib.WindowAccess.CustomWindows;

public class EditListWindow : Window
{
    protected List<string> ListToEdit { get; set; }
    
    public EditListWindow(List<string> listToEdit)
    {
        this.ListToEdit = listToEdit;
    }
}