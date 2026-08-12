using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.ViewModels.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void MenuCommands_InvokeProvidedDelegates()
    {
        int openFilters = 0;
        int openKeybinds = 0;
        int openSettings = 0;
        int openAbout = 0;
        int openDataFolder = 0;
        int clearConfig = 0;
        int resetConfig = 0;
        int resetAllPreviews = 0;
        string? blacklisted = null;
        string? tempBlacklisted = null;

        var sut = CreateSut(
            () => openFilters++,
            () => openKeybinds++,
            () => openSettings++,
            () => openAbout++,
            () => openDataFolder++,
            () => clearConfig++,
            () => resetConfig++,
            () => resetAllPreviews++,
            value => blacklisted = value,
            value => tempBlacklisted = value,
            _ => Task.CompletedTask
        );

        sut.OpenFiltersCommand.Execute(null);
        sut.OpenKeybindsCommand.Execute(null);
        sut.OpenSettingsCommand.Execute(null);
        sut.OpenAboutCommand.Execute(null);
        sut.OpenDataFolderCommand.Execute(null);
        sut.ClearConfigCommand.Execute(null);
        sut.ResetConfigCommand.Execute(null);
        sut.ResetAllPreviewsCommand.Execute(null);

        Assert.Equal(1, openFilters);
        Assert.Equal(1, openKeybinds);
        Assert.Equal(1, openSettings);
        Assert.Equal(1, openAbout);
        Assert.Equal(1, openDataFolder);
        Assert.Equal(1, clearConfig);
        Assert.Equal(1, resetConfig);
        Assert.Equal(1, resetAllPreviews);

        sut.AddToBlacklistCommand.Execute("Some Window");
        sut.AddToTemporaryBlacklistCommand.Execute("window-42");

        sut.WindowList.Dispose();

        Assert.Equal("Some Window", blacklisted);
        Assert.Equal("window-42", tempBlacklisted);
    }

    [Fact]
    public void ResetAllPreviewsCommand_CanExecuteReflectsAvailability()
    {
        bool available = false;
        var sut = CreateSut(
            canResetAllPreviews: () => available
        );

        sut.SetPreviewResetAvailability(false);
        Assert.False(sut.ResetAllPreviewsCommand.CanExecute(null));

        available = true;
        sut.SetPreviewResetAvailability(true);
        Assert.True(sut.ResetAllPreviewsCommand.CanExecute(null));

        sut.WindowList.Dispose();
    }

    [Fact]
    public async Task RenameWindowCommand_InvokesAsyncDelegate()
    {
        string? renamedWindowId = null;
        var sut = CreateSut(renameWindow: windowId =>
        {
            renamedWindowId = windowId;
            return Task.CompletedTask;
        });

        await sut.RenameWindowCommand.ExecuteAsync("window-42");

        sut.WindowList.Dispose();

        Assert.Equal("window-42", renamedWindowId);
    }

    [Fact]
    public void WindowList_IsExposed()
    {
        var sut = CreateSut();

        Assert.NotNull(sut.WindowList);
        Assert.NotNull(sut.WindowList.WindowsConfigs);

        sut.WindowList.Dispose();
    }

    private static MainWindowViewModel CreateSut(
        Action? openFilters = null,
        Action? openKeybinds = null,
        Action? openSettings = null,
        Action? openAbout = null,
        Action? openDataFolder = null,
        Action? clearConfig = null,
        Action? resetConfig = null,
        Action? resetAllPreviews = null,
        Action<string?>? addToBlacklist = null,
        Action<string?>? addToTemporaryBlacklist = null,
        Func<string?, Task>? renameWindow = null,
        Func<bool>? canResetAllPreviews = null
    )
    {
        var windowList = new WindowListViewModel(
            new FakeWindowSnapshotProvider([]),
            new FakeWindowFilterSettingsProvider(
                new WindowFilterSettings(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    []
                )
            ),
            new ImmediateViewModelDispatcher()
        );

        return new MainWindowViewModel(
            windowList,
            openFilters ?? (() => { }),
            openKeybinds ?? (() => { }),
            openSettings ?? (() => { }),
            openAbout ?? (() => { }),
            openDataFolder ?? (() => { }),
            clearConfig ?? (() => { }),
            resetConfig ?? (() => { }),
            canResetAllPreviews ?? (() => true),
            resetAllPreviews ?? (() => { }),
            addToBlacklist ?? (_ => { }),
            addToTemporaryBlacklist ?? (_ => { }),
            renameWindow ?? (_ => Task.CompletedTask)
        );
    }

    private sealed class FakeWindowSnapshotProvider(IReadOnlyCollection<WindowConfig> windows)
        : IWindowSnapshotProvider
    {
        public Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(windows);
        }
    }

    private sealed class FakeWindowFilterSettingsProvider(WindowFilterSettings settings)
        : IWindowFilterSettingsProvider
    {
        public WindowFilterSettings GetSettings()
        {
            return settings;
        }
    }
}