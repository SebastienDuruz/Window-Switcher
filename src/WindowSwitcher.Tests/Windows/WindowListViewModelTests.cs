using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.ViewModels.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class WindowListViewModelTests
{
    [Fact]
    public async Task FetchWindowsWithFiltersAsync_AppliesInjectedFilterSettings()
    {
        using var sut = new WindowListViewModel(
            new FakeWindowSnapshotProvider([
                new WindowConfig { WindowId = "1", WindowTitle = "Visual Studio Code" },
                new WindowConfig { WindowId = "2", WindowTitle = "Browser" },
                new WindowConfig { WindowId = "3", WindowTitle = "Blocked Code" },
            ]),
            new FakeWindowFilterSettingsProvider(
                new WindowFilterSettings(
                    new HashSet<string>(["Blocked Code"], StringComparer.OrdinalIgnoreCase),
                    ["Code"]
                )
            ),
            new ImmediateViewModelDispatcher()
        );

        await sut.FetchWindowsWithFiltersAsync();

        WindowConfig window = Assert.Single(sut.WindowsConfigs);
        Assert.Equal("1", window.WindowId);
    }

    [Fact]
    public async Task FetchWindowsWithFiltersAsync_KeepsWindows_WhenSnapshotIsUnavailable()
    {
        var snapshotProvider = new FakeWindowSnapshotProvider([
            new WindowConfig { WindowId = "1", WindowTitle = "Game client" },
        ]);
        using var sut = CreateViewModel(snapshotProvider);
        await sut.FetchWindowsWithFiltersAsync();
        WindowConfig window = Assert.Single(sut.WindowsConfigs);
        Assert.True(sut.TrySelectWindowById("1"));

        snapshotProvider.Windows = null;
        await sut.FetchWindowsWithFiltersAsync();

        Assert.Same(window, Assert.Single(sut.WindowsConfigs));
        Assert.Same(window, sut.SelectedWindow);
    }

    [Fact]
    public async Task FetchWindowsWithFiltersAsync_RemovesWindows_WhenSnapshotIsEmpty()
    {
        var snapshotProvider = new FakeWindowSnapshotProvider([
            new WindowConfig { WindowId = "1", WindowTitle = "Game client" },
        ]);
        using var sut = CreateViewModel(snapshotProvider);
        await sut.FetchWindowsWithFiltersAsync();
        Assert.Single(sut.WindowsConfigs);

        snapshotProvider.Windows = [];
        await sut.FetchWindowsWithFiltersAsync();

        Assert.Empty(sut.WindowsConfigs);
    }

    private static WindowListViewModel CreateViewModel(IWindowSnapshotProvider snapshotProvider)
    {
        return new WindowListViewModel(
            snapshotProvider,
            new FakeWindowFilterSettingsProvider(
                new WindowFilterSettings(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    ["Game"]
                )
            ),
            new ImmediateViewModelDispatcher()
        );
    }

    private sealed class FakeWindowSnapshotProvider(IReadOnlyCollection<WindowConfig>? windows)
        : IWindowSnapshotProvider
    {
        private IReadOnlyCollection<WindowConfig>? _windows = windows;

        internal IReadOnlyCollection<WindowConfig>? Windows
        {
            get => Volatile.Read(ref _windows);
            set => Volatile.Write(ref _windows, value);
        }

        public Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Windows);
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
