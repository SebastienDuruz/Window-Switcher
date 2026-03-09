using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class FiltersWindow : Window
{
    private readonly UtilityWindowService _windowService = new();
    private readonly PrefixListService _whitelistService;
    private readonly PrefixListService _blacklistService;

    public FiltersWindow(List<string> whitelistPrefixes, List<string> blacklistPrefixes)
    {
        ArgumentNullException.ThrowIfNull(whitelistPrefixes);
        ArgumentNullException.ThrowIfNull(blacklistPrefixes);

        InitializeComponent();

        _whitelistService = new PrefixListService(
            whitelistPrefixes,
            StaticData.PrefixWindowType.whitelist
        );
        _blacklistService = new PrefixListService(
            blacklistPrefixes,
            StaticData.PrefixWindowType.blacklist
        );

        Closing += OnClosing;

        PopulateList(WhitelistListBox, _whitelistService.GetPrefixesSnapshot());
        PopulateList(BlacklistListBox, _blacklistService.GetPrefixesSnapshot());
    }

    public void ShowPrefixesTab()
    {
        FiltersTabControl.SelectedIndex = 0;
        ShowWindow();
    }

    public void ShowBlacklistTab()
    {
        FiltersTabControl.SelectedIndex = 1;
        ShowWindow();
    }

    public bool HasBlacklistPrefixStartingWith(string prefix)
    {
        return _blacklistService.ContainsPrefixStartingWith(prefix);
    }

    public bool TryAddBlacklistPrefix(string prefix)
    {
        return TryAddPrefix(_blacklistService, BlacklistListBox, BlacklistTextBox, prefix);
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

    private void AddWhitelistClick(object? sender, RoutedEventArgs e)
    {
        _ = TryAddPrefix(_whitelistService, WhitelistListBox, WhitelistTextBox, WhitelistTextBox.Text);
    }

    private void WhitelistTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _ = TryAddPrefix(_whitelistService, WhitelistListBox, WhitelistTextBox, WhitelistTextBox.Text);
        e.Handled = true;
    }

    private void AddBlacklistClick(object? sender, RoutedEventArgs e)
    {
        _ = TryAddPrefix(_blacklistService, BlacklistListBox, BlacklistTextBox, BlacklistTextBox.Text);
    }

    private void BlacklistTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _ = TryAddPrefix(_blacklistService, BlacklistListBox, BlacklistTextBox, BlacklistTextBox.Text);
        e.Handled = true;
    }

    private void WhitelistListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RemoveSelectedPrefix(_whitelistService, WhitelistListBox, e);
    }

    private void BlacklistListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RemoveSelectedPrefix(_blacklistService, BlacklistListBox, e);
    }

    private static void PopulateList(ListBox listBox, IReadOnlyCollection<string> prefixes)
    {
        foreach (string prefix in prefixes)
            AddPrefixToList(listBox, prefix);
    }

    private static void AddPrefixToList(ListBox listBox, string prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            listBox.Items.Add(
                new ListBoxItem
                {
                    Content = prefix.ToLowerInvariant(),
                    Height = 22,
                    FontSize = 14,
                    Padding = new Thickness(8, 2),
                }
            );
        });
    }

    private static void DeletePrefixFromList(ListBox listBox, ListBoxItem prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            listBox.SelectedIndex = -1;
            listBox.Items.Remove(prefix);
        });
    }

    private static bool TryAddPrefix(
        PrefixListService prefixListService,
        ListBox listBox,
        TextBox textBox,
        string? value
    )
    {
        if (!prefixListService.TryAddPrefix(value, out string normalizedPrefix))
            return false;

        AddPrefixToList(listBox, normalizedPrefix);
        textBox.Text = string.Empty;
        return true;
    }

    private static void RemoveSelectedPrefix(
        PrefixListService prefixListService,
        ListBox listBox,
        SelectionChangedEventArgs e
    )
    {
        if (e.AddedItems.Count == 0)
            return;

        if (e.AddedItems[0] is not ListBoxItem selectedPrefix)
            return;

        bool isRemoved = prefixListService.TryRemovePrefix(selectedPrefix.Content as string, out _);
        if (!isRemoved)
            return;

        DeletePrefixFromList(listBox, selectedPrefix);
    }
}
