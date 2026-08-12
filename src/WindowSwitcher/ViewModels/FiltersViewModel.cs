using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the filters window: a whitelist and a blacklist of window title prefixes.
/// </summary>
public partial class FiltersViewModel : ObservableObject
{
    public PrefixListViewModel Whitelist { get; }
    public PrefixListViewModel Blacklist { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public FiltersViewModel(PrefixListViewModel whitelist, PrefixListViewModel blacklist)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(blacklist);

        Whitelist = whitelist;
        Blacklist = blacklist;
    }
}