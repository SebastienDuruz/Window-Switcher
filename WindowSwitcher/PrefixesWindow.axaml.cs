using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WindowSwitcherLib.Data.CustomWindows;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows;

namespace WindowSwitcher;

public partial class PrefixesWindow : EditListWindow, IDestroyableWindow
{
    public bool ToDestroy { get; set; } = false;

    public PrefixesWindow(List<string> listToEdit, StaticData.PrefixWindowType prefixWindowType, string windowTitle) : base(listToEdit, prefixWindowType)
    {
        InitializeComponent();
        Closing += OnClosing;
        Title = windowTitle;

        foreach (string prefix in ListToEdit)
            AddPrefixToList(prefix);
    }

    public void Destroy()
    {
        ToDestroy = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !ToDestroy;
        Hide();
    }

    private void AddPrefixClick(object? sender, RoutedEventArgs e)
    {
        PrefixTextBox.Text = PrefixTextBox.Text.ToLower();
        if (!string.IsNullOrWhiteSpace(PrefixTextBox.Text) && !ListToEdit.Contains(PrefixTextBox.Text))
        {
            ListToEdit.Add(PrefixTextBox.Text);
            ConfigFileAccessor.GetInstance().WriteUserSettings();
            AddPrefixToList(PrefixTextBox.Text);
            PrefixTextBox.Text = "";
            
            SavePrefixList();
        }
    }

    public void AddPrefixToList(string prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            PrefixListBox.Items.Add(new ListBoxItem()
            {
                Content = prefix.ToLower(),
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
        
        DeletePrefixFromList(selectedPrefix);
        
        ListToEdit.Remove(((string)selectedPrefix.Content!).ToLower());
        
        SavePrefixList();
    }

    private void SavePrefixList()
    {
        switch (PrefixWindowType)
        {
            case StaticData.PrefixWindowType.whitelist:
                ConfigFileAccessor.GetInstance().SavePrefixesList(ListToEdit);
                break;
            case StaticData.PrefixWindowType.blacklist:
                ConfigFileAccessor.GetInstance().SaveBlacklist(ListToEdit);
                break;
        }
    }
}