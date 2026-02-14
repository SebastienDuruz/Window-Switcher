using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcher.Controls;
using WindowSwitcher.Windows.Services;
using WindowSwitcherLib.Data;

namespace WindowSwitcher.Windows;

public partial class PrefixesWindow : EditListWindow
{
    private readonly UtilityWindowService _windowService = new();
    private readonly PrefixListService _prefixListService;

    public PrefixesWindow(List<string> listToEdit, StaticData.PrefixWindowType prefixWindowType, string windowTitle) : base(listToEdit, prefixWindowType)
    {
        InitializeComponent();
        _prefixListService = new PrefixListService(ListToEdit, PrefixWindowType);
        Closing += OnClosing;
        Title = windowTitle;

        foreach (string prefix in _prefixListService.GetPrefixesSnapshot())
            AddPrefixToList(prefix);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowService.HandleClosing(this, e, hideWhenCanceled: true, hideWhenAllowed: true);
    }

    private void AddPrefixClick(object? sender, RoutedEventArgs e)
    {
        if (!_prefixListService.TryAddPrefix(PrefixTextBox.Text, out string normalizedPrefix))
            return;

        AddPrefixToList(normalizedPrefix);
        PrefixTextBox.Text = string.Empty;
    }

    public void AddPrefixToList(string prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            PrefixListBox.Items.Add(new ListBoxItem()
            {
                Content = prefix.ToLower(),
                Height = 22,
                FontSize = 14,
                Padding = new Thickness(8, 2)
            });
        });
    }

    private void DeletePrefixFromList(ListBoxItem prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            PrefixListBox.SelectedIndex = -1;
            PrefixListBox.Items.Remove(prefix);
        });
    }

    private void PrefixListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;

        ListBoxItem? selectedPrefix = e.AddedItems[0] as ListBoxItem;
        if (selectedPrefix == null)
            return;

        bool isRemoved = _prefixListService.TryRemovePrefix(selectedPrefix.Content as string, out _);
        if (!isRemoved)
            return;

        DeletePrefixFromList(selectedPrefix);
    }

    public bool HasPrefixStartingWith(string prefix)
    {
        return _prefixListService.ContainsPrefixStartingWith(prefix);
    }

    public bool TryAddPrefix(string prefix)
    {
        if (!_prefixListService.TryAddPrefix(prefix, out string normalizedPrefix))
            return false;

        AddPrefixToList(normalizedPrefix);
        return true;
    }
}
