using Avalonia.Controls;

namespace WindowSwitcherLib.Data.CustomWindows;

public class EditListWindow(List<string> listToEdit, StaticData.PrefixWindowType prefixWindowType)
    : Window
{
    public List<string> ListToEdit { get; private set; } = listToEdit;
    public StaticData.PrefixWindowType PrefixWindowType { get; private set; } = prefixWindowType;
}