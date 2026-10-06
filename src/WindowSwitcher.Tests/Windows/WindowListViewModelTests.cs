using Serilog.Events;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.Tests.TestLogging;
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

    [Fact]
    public async Task PeriodicRefresh_LogsErrorAndKeepsRefreshing_WhenSnapshotProviderThrows()
    {
        var failure = new InvalidOperationException("snapshot failure");
        var snapshotProvider = new FakeWindowSnapshotProvider([
            new WindowConfig { WindowId = "1", WindowTitle = "Game client" },
        ])
        {
            Failure = failure,
        };
        Serilog.ILogger logger = TestLogger.Create(out CollectingSink sink);
        using var sut = CreateViewModel(snapshotProvider, logger);

        LogEvent errorEvent = await WaitForAsync(() =>
            sink.AtLevel(LogEventLevel.Error).FirstOrDefault()
        );
        Assert.Same(failure, errorEvent.Exception);
        Assert.Contains("Window list refresh failed", errorEvent.RenderMessage());

        snapshotProvider.Failure = null;
        WindowConfig window = await WaitForAsync(() => sut.WindowsConfigs.FirstOrDefault());

        Assert.Equal("1", window.WindowId);
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            T? value = probe();
            if (value is not null)
                return value;

            await Task.Delay(25, timeout.Token);
        }
    }

    private static WindowListViewModel CreateViewModel(
        IWindowSnapshotProvider snapshotProvider,
        Serilog.ILogger? logger = null
    )
    {
        return new WindowListViewModel(
            snapshotProvider,
            new FakeWindowFilterSettingsProvider(
                new WindowFilterSettings(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    ["Game"]
                )
            ),
            new ImmediateViewModelDispatcher(),
            logger
        );
    }

    private sealed class FakeWindowSnapshotProvider(IReadOnlyCollection<WindowConfig>? windows)
        : IWindowSnapshotProvider
    {
        private IReadOnlyCollection<WindowConfig>? _windows = windows;
        private Exception? _failure;

        internal IReadOnlyCollection<WindowConfig>? Windows
        {
            get => Volatile.Read(ref _windows);
            set => Volatile.Write(ref _windows, value);
        }

        internal Exception? Failure
        {
            get => Volatile.Read(ref _failure);
            set => Volatile.Write(ref _failure, value);
        }

        public Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
            CancellationToken cancellationToken = default
        )
        {
            Exception? failure = Failure;
            if (failure is not null)
                return Task.FromException<IReadOnlyCollection<WindowConfig>?>(failure);

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
