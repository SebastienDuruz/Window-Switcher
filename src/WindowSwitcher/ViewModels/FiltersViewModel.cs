using System;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the filters page: a whitelist and a blacklist of window title prefixes.
/// </summary>
public sealed class FiltersViewModel
{
    public PrefixListViewModel Whitelist { get; }
    public PrefixListViewModel Blacklist { get; }

    public FiltersViewModel(PrefixListViewModel whitelist, PrefixListViewModel blacklist)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(blacklist);

        Whitelist = whitelist;
        Blacklist = blacklist;
    }
}
