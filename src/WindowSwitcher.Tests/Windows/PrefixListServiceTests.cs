using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class PrefixListServiceTests
{
    [Fact]
    public void TryAddPrefix_StoresNormalizedPrefixAndPersistsTheList()
    {
        List<List<string>> saved = [];
        var sut = new PrefixListService([], prefixes => saved.Add([.. prefixes]));

        bool added = sut.TryAddPrefix("  Firefox ", out string normalized);

        Assert.True(added);
        Assert.Equal("firefox", normalized);
        Assert.Equal(["firefox"], sut.GetPrefixes());
        Assert.Equal(["firefox"], Assert.Single(saved));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("FIREFOX")] // already present once normalized
    public void TryAddPrefix_RejectsBlankOrDuplicatePrefixesWithoutPersisting(string? value)
    {
        int saves = 0;
        var sut = new PrefixListService(["firefox"], _ => saves++);

        Assert.False(sut.TryAddPrefix(value, out _));
        Assert.Equal(["firefox"], sut.GetPrefixes());
        Assert.Equal(0, saves);
    }

    [Fact]
    public void TryRemovePrefix_RemovesNormalizedPrefixAndPersistsTheList()
    {
        List<List<string>> saved = [];
        var sut = new PrefixListService(["firefox", "code"], prefixes => saved.Add([.. prefixes]));

        bool removed = sut.TryRemovePrefix(" FireFox", out string normalized);

        Assert.True(removed);
        Assert.Equal("firefox", normalized);
        Assert.Equal(["code"], Assert.Single(saved));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public void TryRemovePrefix_ReturnsFalseWithoutPersisting(string? value)
    {
        int saves = 0;
        var sut = new PrefixListService(["firefox"], _ => saves++);

        Assert.False(sut.TryRemovePrefix(value, out _));
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData("fire", true)]
    [InlineData(" FIRE ", true)]
    [InlineData("firefox", true)]
    [InlineData("fox", false)]
    [InlineData("", false)]
    public void ContainsPrefixStartingWith_MatchesStoredPrefixesByTheirStart(
        string candidate,
        bool expected
    )
    {
        var sut = new PrefixListService(["firefox"], _ => { });

        Assert.Equal(expected, sut.ContainsPrefixStartingWith(candidate));
    }

    [Fact]
    public void ReplaceAll_ReplacesThePrefixesWithoutPersisting()
    {
        int saves = 0;
        var sut = new PrefixListService(["firefox", "code"], _ => saves++);

        sut.ReplaceAll(["terminal"]);

        Assert.Equal(["terminal"], sut.GetPrefixes());
        Assert.Equal(0, saves);
    }

    [Fact]
    public void GetPrefixes_ReturnsASnapshot()
    {
        var sut = new PrefixListService(["firefox"], _ => { });
        IReadOnlyCollection<string> snapshot = sut.GetPrefixes();

        sut.TryAddPrefix("code", out _);

        Assert.Equal(["firefox"], snapshot);
    }
}
