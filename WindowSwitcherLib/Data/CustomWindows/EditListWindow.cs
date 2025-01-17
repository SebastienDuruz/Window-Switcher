using Avalonia.Controls;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcherLib.Data.CustomWindows;

public class EditListWindow : Window
{
    public List<string> ListToEdit { get; private set; }
    public StaticData.PrefixWindowType PrefixWindowType { get; private set; }
    
    public EditListWindow(List<string> listToEdit, StaticData.PrefixWindowType prefixWindowType)
    {
        ListToEdit = listToEdit;
        PrefixWindowType = prefixWindowType;
    }
}