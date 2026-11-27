using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WindowSwitcher.ViewModels;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class SettingsWindowViewModelTests
{
    [Fact]
    public void SelectedPage_StartsOnGeneral()
    {
        var sut = CreateSut();

        Assert.Equal(SettingsPage.General, sut.SelectedPage);
        Assert.Equal(0, sut.SelectedPageIndex);
    }

    [Fact]
    public void ShowPage_SelectsThePageAndReportsItShown()
    {
        var shown = new List<SettingsPage>();
        var sut = CreateSut(pageShown: shown.Add);

        sut.ShowPage(SettingsPage.Keybinds);

        Assert.Equal(SettingsPage.Keybinds, sut.SelectedPage);
        Assert.Equal((int)SettingsPage.Keybinds, sut.SelectedPageIndex);
        Assert.Equal([SettingsPage.Keybinds], shown);
    }

    [Fact]
    public void ShowPage_ReportsTheCurrentPageAgain()
    {
        var shown = new List<SettingsPage>();
        var sut = CreateSut(pageShown: shown.Add);
        sut.ShowPage(SettingsPage.About);

        sut.ShowPage(SettingsPage.About);

        Assert.Equal([SettingsPage.About, SettingsPage.About], shown);
    }

    [Fact]
    public void ShowPage_RejectsAnUnknownPage()
    {
        var sut = CreateSut();

        Assert.Throws<ArgumentOutOfRangeException>(() => sut.ShowPage((SettingsPage)42));
    }

    [Fact]
    public void SelectingFromTheSidebar_ReportsThePageShown()
    {
        var shown = new List<SettingsPage>();
        var sut = CreateSut(pageShown: shown.Add);

        sut.SelectedPageIndex = (int)SettingsPage.Filters;

        Assert.Equal(SettingsPage.Filters, sut.SelectedPage);
        Assert.Equal([SettingsPage.Filters], shown);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task ResetLayoutCommand_ResetsOnlyWhenConfirmed(bool confirmed, int expectedResets)
    {
        ConfirmationRequest? request = null;
        int resets = 0;
        var sut = CreateSut(
            resetLayout: () => resets++,
            confirm: value =>
            {
                request = value;
                return Task.FromResult(confirmed);
            }
        );

        await sut.ResetLayoutCommand.ExecuteAsync(null);

        Assert.Same(SettingsWindowViewModel.ResetLayoutConfirmation, request);
        Assert.Equal(expectedResets, resets);
    }

    [Fact]
    public async Task ResetSettingsCommand_ResetsAndReloadsWhenConfirmed()
    {
        var repository = new SettingsViewModelTests.FakeSettingsRepository();
        int applyCount = 0;
        var settings = new SettingsViewModel(repository, () => applyCount++, _ => { });
        ConfirmationRequest? request = null;
        int resets = 0;
        var sut = CreateSut(
            settings,
            resetSettings: () => resets++,
            confirm: value =>
            {
                request = value;
                return Task.FromResult(true);
            }
        );

        await sut.ResetSettingsCommand.ExecuteAsync(null);

        Assert.Same(SettingsWindowViewModel.ResetSettingsConfirmation, request);
        Assert.Equal(1, resets);
        Assert.Equal(1, applyCount);
    }

    [Fact]
    public async Task ResetSettingsCommand_DoesNothingWhenCancelled()
    {
        int applyCount = 0;
        var settings = new SettingsViewModel(
            new SettingsViewModelTests.FakeSettingsRepository(),
            () => applyCount++,
            _ => { }
        );
        int resets = 0;
        var sut = CreateSut(
            settings,
            resetSettings: () => resets++,
            confirm: _ => Task.FromResult(false)
        );

        await sut.ResetSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0, resets);
        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void ResetAllPreviews_IsAvailableOnlyWithACaptureReset()
    {
        int resets = 0;
        var available = CreateSut(resetAllPreviews: () => resets++);
        var unavailable = CreateSut(resetAllPreviews: null);

        Assert.True(available.CanResetAllPreviews);
        Assert.True(available.ResetAllPreviewsCommand.CanExecute(null));
        available.ResetAllPreviewsCommand.Execute(null);
        Assert.Equal(1, resets);

        Assert.False(unavailable.CanResetAllPreviews);
        Assert.False(unavailable.ResetAllPreviewsCommand.CanExecute(null));
    }

    private static SettingsWindowViewModel CreateSut(
        SettingsViewModel? settings = null,
        Action? resetLayout = null,
        Action? resetSettings = null,
        Action? resetAllPreviews = null,
        Func<ConfirmationRequest, Task<bool>>? confirm = null,
        Action<SettingsPage>? pageShown = null
    )
    {
        return new SettingsWindowViewModel(
            settings
                ?? new SettingsViewModel(
                    new SettingsViewModelTests.FakeSettingsRepository(),
                    () => { },
                    _ => { }
                ),
            resetLayout ?? (() => { }),
            resetSettings ?? (() => { }),
            resetAllPreviews,
            confirm ?? (_ => Task.FromResult(true)),
            pageShown ?? (_ => { })
        );
    }
}

public sealed class ConfirmationViewModelTests
{
    private static readonly ConfirmationRequest Request = new("Title", "Message", "Confirm");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Commands_CompleteWithTheUserChoice(bool confirm)
    {
        var sut = new ConfirmationViewModel(Request);
        var results = new List<bool>();
        sut.Completed += (_, confirmed) => results.Add(confirmed);

        (confirm ? sut.ConfirmCommand : sut.CancelCommand).Execute(null);

        Assert.True(sut.IsCompleted);
        Assert.Equal([confirm], results);
    }

    [Fact]
    public void Completed_IsRaisedOnlyOnce()
    {
        var sut = new ConfirmationViewModel(Request);
        var results = new List<bool>();
        sut.Completed += (_, confirmed) => results.Add(confirmed);

        sut.ConfirmCommand.Execute(null);
        sut.CancelCommand.Execute(null);

        Assert.Equal([true], results);
    }

    [Theory]
    [InlineData("", "Message", "Confirm")]
    [InlineData("Title", " ", "Confirm")]
    [InlineData("Title", "Message", "")]
    public void Request_RejectsEmptyText(string title, string message, string confirmLabel)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ConfirmationRequest(title, message, confirmLabel)
        );
    }
}
