using System.Collections.Generic;
using Avalonia.Controls;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Common;

namespace WindowSwitcher.Controls;

public class EditListWindow(List<string> listToEdit, StaticData.PrefixWindowType prefixWindowType)
    : Window
{
    public List<string> ListToEdit { get; private set; } = listToEdit;
    public StaticData.PrefixWindowType PrefixWindowType { get; private set; } = prefixWindowType;
}