using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs a single prefix list (whitelist or blacklist) and exposes the add/remove
/// operations through commands.
/// </summary>
public partial class PrefixListViewModel : ObservableObject
{
    private readonly IPrefixListService _prefixListService;

    public ObservableCollection<string> Prefixes { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newPrefix = string.Empty;

    public IRelayCommand AddCommand { get; }

    /// <summary>
    /// Removes the prefix passed as the command parameter.
    /// </summary>
    public IRelayCommand<string> RemoveCommand { get; }

    public PrefixListViewModel(IPrefixListService prefixListService)
    {
        ArgumentNullException.ThrowIfNull(prefixListService);

        _prefixListService = prefixListService;
        Prefixes = new ObservableCollection<string>(prefixListService.GetPrefixes());
        AddCommand = new RelayCommand(AddPrefix, () => !string.IsNullOrWhiteSpace(NewPrefix));
        RemoveCommand = new RelayCommand<string>(RemovePrefix);
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

    /// <summary>
    /// Shows <paramref name="prefixes"/> after the configuration was replaced as a whole
    /// (reset to defaults). Nothing is persisted.
    /// </summary>
    public void Reload(IReadOnlyCollection<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        _prefixListService.ReplaceAll(prefixes);
        Prefixes.Clear();
        foreach (string prefix in _prefixListService.GetPrefixes())
            Prefixes.Add(prefix);
        NewPrefix = string.Empty;
    }

    private void AddPrefix()
    {
        _ = TryAddPrefix(NewPrefix);
        NewPrefix = string.Empty;
    }

    private void RemovePrefix(string? value)
    {
        if (value is null || !_prefixListService.TryRemovePrefix(value, out _))
            return;

        Prefixes.Remove(value);
    }
}
