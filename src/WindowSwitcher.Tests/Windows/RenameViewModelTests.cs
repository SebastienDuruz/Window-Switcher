using System.Threading.Tasks;
using WindowSwitcher.ViewModels;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class RenameViewModelTests
{
    [Fact]
    public async Task Confirm_CompletesSessionWithTrimmedTitle()
    {
        var sut = new RenameViewModel();
        Task<string?> session = sut.StartSessionAsync("Initial");

        sut.WindowTitle = "  New Title  ";
        sut.ConfirmCommand.Execute(null);

        string? result = await session;
        Assert.Equal("New Title", result);
        Assert.Equal("New Title", sut.Result);
    }

    [Fact]
    public async Task Confirm_WithWhitespaceTitle_CompletesWithNull()
    {
        var sut = new RenameViewModel();
        Task<string?> session = sut.StartSessionAsync("Initial");

        sut.WindowTitle = "   ";
        sut.ConfirmCommand.Execute(null);

        Assert.Null(await session);
    }

    [Fact]
    public async Task Cancel_CompletesSessionWithNull()
    {
        var sut = new RenameViewModel();
        Task<string?> session = sut.StartSessionAsync("Initial");

        sut.CancelCommand.Execute(null);

        Assert.Null(await session);
    }

    [Fact]
    public async Task CancelSession_CompletesPendingSessionWithNull()
    {
        var sut = new RenameViewModel();
        Task<string?> session = sut.StartSessionAsync("Initial");

        sut.CancelSession();

        Assert.Null(await session);
    }

    [Fact]
    public async Task RestoreOriginal_CompletesSessionWithTheOriginalTitle()
    {
        var sut = new RenameViewModel();
        Task<string?> session = sut.StartSessionAsync("Client 2", "World of Warcraft");
        Assert.True(sut.HasOriginalTitle);
        Assert.True(sut.RestoreOriginalCommand.CanExecute(null));

        sut.RestoreOriginalCommand.Execute(null);

        Assert.Equal("World of Warcraft", await session);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RestoreOriginal_IsUnavailableWithoutAnOriginalTitle(string? originalTitle)
    {
        var sut = new RenameViewModel();
        _ = sut.StartSessionAsync("Client 2", originalTitle);

        Assert.False(sut.HasOriginalTitle);
        Assert.False(sut.RestoreOriginalCommand.CanExecute(null));
    }

    [Fact]
    public void StartSession_ReplacesTheOriginalTitleOfThePreviousSession()
    {
        var sut = new RenameViewModel();
        _ = sut.StartSessionAsync("Client 2", "World of Warcraft");

        _ = sut.StartSessionAsync("Terminal");

        Assert.False(sut.HasOriginalTitle);
    }
}

public sealed class OriginalTitleTrackerTests
{
    [Fact]
    public void Resolve_RemembersTheTitleBeforeTheFirstRename()
    {
        string original = OriginalTitleTracker.Resolve("World of Warcraft", null, "Client 2");

        Assert.Equal("World of Warcraft", original);
    }

    [Fact]
    public void Resolve_KeepsTheStoredOriginalAcrossRenames()
    {
        string original = OriginalTitleTracker.Resolve("Client 2", "World of Warcraft", "Main");

        Assert.Equal("World of Warcraft", original);
    }

    [Theory]
    [InlineData("Client 2", "World of Warcraft", "World of Warcraft")]
    [InlineData("World of Warcraft", null, "World of Warcraft")]
    public void Resolve_ForgetsTheOriginalWhenItIsRestored(
        string titleBeforeRename,
        string? storedOriginal,
        string newTitle
    )
    {
        Assert.Equal(
            string.Empty,
            OriginalTitleTracker.Resolve(titleBeforeRename, storedOriginal, newTitle)
        );
    }
}
