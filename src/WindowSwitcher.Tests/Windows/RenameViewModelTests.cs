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
}