using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs a single prefix list (whitelist or blacklist) and exposes the add/remove
/// operations through commands. Selecting an entry removes it.
/// </summary>
public partial class PrefixListViewModel : ObservableObject
{
    private readonly IPrefixListService _prefixListService;

    public ObservableCollection<string> Prefixes { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newPrefix = string.Empty;

    [ObservableProperty]
    private string? _selectedPrefix;

    public IRelayCommand AddCommand { get; }

    public PrefixListViewModel(IPrefixListService prefixListService)
    {
        ArgumentNullException.ThrowIfNull(prefixListService);

        _prefixListService = prefixListService;
        Prefixes = new ObservableCollection<string>(prefixListService.GetPrefixes());
        AddCommand = new RelayCommand(AddPrefix, () => !string.IsNullOrWhiteSpace(NewPrefix));
    }

    public bool TryAddPrefix(string value)
    {
        if (!_prefixListService.TryAddPrefix(value, out string normalizedPrefix))
            return false;

        Prefixes.Add(normalizedPrefix);
        return true;
    }

    public bool ContainsPrefixStartingWith(string candidate)
    {
        return _prefixListService.ContainsPrefixStartingWith(candidate);
    }

    partial void OnSelectedPrefixChanged(string? value)
    {
        if (value is null)
            return;

        RemoveSelectedPrefix(value);
    }

    private void AddPrefix()
    {
        _ = TryAddPrefix(NewPrefix);
        NewPrefix = string.Empty;
    }

    private void RemoveSelectedPrefix(string value)
    {
        if (!_prefixListService.TryRemovePrefix(value, out _))
            return;

        SelectedPrefix = null;
        Prefixes.Remove(value);
    }
}