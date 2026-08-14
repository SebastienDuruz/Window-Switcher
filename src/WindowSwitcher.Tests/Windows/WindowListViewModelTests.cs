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
