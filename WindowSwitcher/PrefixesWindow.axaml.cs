using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;
using WindowSwitcherLib.WindowAccess.CustomWindows;

namespace WindowSwitcher;

public partial class PrefixesWindow : EditListWindow, IDestroyableWindow
{
    public bool ToDestroy { get; set; } = false;

    public PrefixesWindow(List<string> listToEdit, string windowTitle) : base(listToEdit)
    {
        InitializeComponent();
        this.Closing += OnClosing;
        this.Title = windowTitle;

        foreach (string prefix in this.ListToEdit)
            AddPrefixToList(prefix);
    }

    public void Destroy()
    {
        this.ToDestroy = true;
        this.Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !ToDestroy;
        this.Hide();
    }

    private void AddPrefixClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(PrefixTextBox.Text) && !this.ListToEdit.Contains(PrefixTextBox.Text.ToLower()))
        {
            this.ListToEdit.Add(PrefixTextBox.Text.ToLower());
            
            ConfigFileAccessor.GetInstance().WriteUserSettings();
            
            AddPrefixToList(PrefixTextBox.Text.ToLower());
            PrefixTextBox.Text = "";
        }
    }

    private void AddPrefixToList(string prefix)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            PrefixListBox.Items.Add(new ListBoxItem()
            {
                Content = prefix,
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
        
        this.ListToEdit.Remove(((string)selectedPrefix.Content!).ToLower());
        ConfigFileAccessor.GetInstance().WriteUserSettings();
    }
}