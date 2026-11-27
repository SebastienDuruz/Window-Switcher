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
        Assert.Contains("visual studio", service.Prefixes);
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
    public void RemoveCommand_RemovesThePrefix()
    {
        var service = new FakePrefixListService();
        service.Prefixes.Add("code");
        service.Prefixes.Add("firefox");
        var sut = new PrefixListViewModel(service);

        sut.RemoveCommand.Execute("code");

        Assert.DoesNotContain("code", sut.Prefixes);
        Assert.DoesNotContain("code", service.Prefixes);
        Assert.Contains("firefox", sut.Prefixes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public void RemoveCommand_IgnoresMissingPrefix(string? value)
    {
        var service = new FakePrefixListService();
        service.Prefixes.Add("code");
        var sut = new PrefixListViewModel(service);

        sut.RemoveCommand.Execute(value);

        Assert.Equal(["code"], sut.Prefixes);
        Assert.Equal(["code"], service.Prefixes);
    }

    [Fact]
    public void Reload_ReplacesTheShownPrefixesAndClearsInput()
    {
        var service = new FakePrefixListService();
        service.Prefixes.Add("old");
        var sut = new PrefixListViewModel(service) { NewPrefix = "draft" };

        sut.Reload(["first", "second"]);

        Assert.Equal(["first", "second"], sut.Prefixes);
        Assert.Equal(["first", "second"], service.Prefixes);
        Assert.Equal(string.Empty, sut.NewPrefix);
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
        var whitelist = new PrefixListViewModel(new FakePrefixListService());
        var blacklist = new PrefixListViewModel(new FakePrefixListService());

        var sut = new FiltersViewModel(whitelist, blacklist);

        Assert.Same(whitelist, sut.Whitelist);
        Assert.Same(blacklist, sut.Blacklist);
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
        return Prefixes.Any(prefix => prefix.StartsWith(candidate, StringComparison.Ordinal));
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

    public void ReplaceAll(IReadOnlyCollection<string> prefixes)
    {
        Prefixes.Clear();
        Prefixes.AddRange(prefixes);
    }
}
