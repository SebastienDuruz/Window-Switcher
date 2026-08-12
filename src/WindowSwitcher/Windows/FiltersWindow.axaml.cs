using System.Collections.Generic;
using Avalonia.Controls;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class FiltersWindow : Window
{
    private readonly UtilityWindowService _windowService = new();
    private FiltersViewModel ViewModel { get; }

    public FiltersWindow(List<string> whitelistPrefixes, List<string> blacklistPrefixes)
    {
        InitializeComponent();

        ViewModel = new FiltersViewModel(
            new PrefixListViewModel(
                new PrefixListService(whitelistPrefixes, StaticData.PrefixWindowType.whitelist)
            ),
            new PrefixListViewModel(
                new PrefixListService(blacklistPrefixes, StaticData.PrefixWindowType.blacklist)
            )
        );
        DataContext = ViewModel;

        Closing += OnClosing;
    }

    public void ShowPrefixesTab()
    {
        ViewModel.SelectedTabIndex = 0;
        ShowWindow();
    }

    public void ShowBlacklistTab()
    {
        ViewModel.SelectedTabIndex = 1;
        ShowWindow();
    }

    public bool HasBlacklistPrefixStartingWith(string prefix)
    {
        return ViewModel.Blacklist.ContainsPrefixStartingWith(prefix);
    }

    public bool TryAddBlacklistPrefix(string prefix)
    {
        return ViewModel.Blacklist.TryAddPrefix(prefix);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowService.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }

    private void ShowWindow()
    {
        if (IsVisible)
        {
            Activate();
            return;
        }

        Show();
    }
}