using System;
using System.Collections.Generic;
using System.Linq;
using WindowSwitcher.ViewModels;
using WindowSwitcher.ViewModels.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class PrefixListViewModelTests
{
    [Fact]
    public void AddCommand_AddsNormalizedPrefixAndClearsInput()
    {
        var service = new FakePrefixListService();
        var sut = new PrefixListViewModel(service);

        sut.NewPrefix = "  Visual Studio  ";
        Assert.True(sut.AddCommand.CanExecute(null));
        sut.AddCommand.Execute(null);

        Assert.Contains("visual studio", sut.Prefixes);
        Assert.True(service.Prefixes.Contains("visual studio"));
        Assert.Equal(string.Empty, sut.NewPrefix);
    }

    [Fact]
    public void AddCommand_DisabledWhenInputIsWhitespace()
    {
        var sut = new PrefixListViewModel(new FakePrefixListService());

        sut.NewPrefix = "   ";

        Assert.False(sut.AddCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingPrefix_RemovesItAndClearsSelection()
    {
        var service = new FakePrefixListService();
        service.Prefixes.Add("code");
        var sut = new PrefixListViewModel(service);

        sut.SelectedPrefix = "code";

        Assert.DoesNotContain("code", sut.Prefixes);
        Assert.Null(sut.SelectedPrefix);
    }

    [Fact]
    public void TryAddPrefix_AddsNormalizedPrefix()
    {
        var service = new FakePrefixListService();
        var sut = new PrefixListViewModel(service);

        bool added = sut.TryAddPrefix("Firefox");

        Assert.True(added);
        Assert.Contains("firefox", sut.Prefixes);
    }
}

public sealed class FiltersViewModelTests
{
    [Fact]
    public void ViewModel_WiresWhitelistAndBlacklist()
    {
        var sut = new FiltersViewModel(
            new PrefixListViewModel(new FakePrefixListService()),
            new PrefixListViewModel(new FakePrefixListService())
        );

        Assert.NotNull(sut.Whitelist);
        Assert.NotNull(sut.Blacklist);
        Assert.Equal(0, sut.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_CanBeSwitched()
    {
        var sut = new FiltersViewModel(
            new PrefixListViewModel(new FakePrefixListService()),
            new PrefixListViewModel(new FakePrefixListService())
        );

        sut.SelectedTabIndex = 1;

        Assert.Equal(1, sut.SelectedTabIndex);
    }
}

internal sealed class FakePrefixListService : IPrefixListService
{
    public List<string> Prefixes { get; } = [];

    public IReadOnlyCollection<string> GetPrefixes()
    {
        return Prefixes.ToArray();
    }

    public bool ContainsPrefixStartingWith(string candidate)
    {
        return Prefixes.Any(prefix =>
            prefix.StartsWith(candidate, StringComparison.Ordinal)
        );
    }

    public bool TryAddPrefix(string? value, out string normalizedPrefix)
    {
        normalizedPrefix = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedPrefix) || Prefixes.Contains(normalizedPrefix))
            return false;

        Prefixes.Add(normalizedPrefix);
        return true;
    }

    public bool TryRemovePrefix(string? value, out string normalizedPrefix)
    {
        normalizedPrefix = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return false;

        return Prefixes.Remove(normalizedPrefix);
    }
}