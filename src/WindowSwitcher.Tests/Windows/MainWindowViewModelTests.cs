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
        int openSettings = 0;
        int openAbout = 0;
        int openConfigFolder = 0;
        int openLogsFolder = 0;
        string? blacklisted = null;
        string? tempBlacklisted = null;

        var sut = CreateSut(
            () => openSettings++,
            () => openAbout++,
            () => openConfigFolder++,
            () => openLogsFolder++,
            value => blacklisted = value,
            value => tempBlacklisted = value,
            _ => Task.CompletedTask
        );

        sut.OpenSettingsCommand.Execute(null);
        sut.OpenAboutCommand.Execute(null);
        sut.OpenConfigFolderCommand.Execute(null);
        sut.OpenLogsFolderCommand.Execute(null);

        Assert.Equal(1, openSettings);
        Assert.Equal(1, openAbout);
        Assert.Equal(1, openConfigFolder);
        Assert.Equal(1, openLogsFolder);

        sut.AddToBlacklistCommand.Execute("Some Window");
        sut.AddToTemporaryBlacklistCommand.Execute("window-42");

        sut.WindowList.Dispose();

        Assert.Equal("Some Window", blacklisted);
        Assert.Equal("window-42", tempBlacklisted);
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
        Action? openSettings = null,
        Action? openAbout = null,
        Action? openConfigFolder = null,
        Action? openLogsFolder = null,
        Action<string?>? addToBlacklist = null,
        Action<string?>? addToTemporaryBlacklist = null,
        Func<string?, Task>? renameWindow = null
    )
    {
        var windowList = new WindowListViewModel(
            new FakeWindowSnapshotProvider([]),
            new FakeWindowFilterSettingsProvider(
                new WindowFilterSettings(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [])
            ),
            new ImmediateViewModelDispatcher()
        );

        return new MainWindowViewModel(
            windowList,
            openSettings ?? (() => { }),
            openAbout ?? (() => { }),
            openConfigFolder ?? (() => { }),
            openLogsFolder ?? (() => { }),
            addToBlacklist ?? (_ => { }),
            addToTemporaryBlacklist ?? (_ => { }),
            renameWindow ?? (_ => Task.CompletedTask)
        );
    }

    private sealed class FakeWindowSnapshotProvider(IReadOnlyCollection<WindowConfig> windows)
        : IWindowSnapshotProvider
    {
        public Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyCollection<WindowConfig>?>(windows);
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
