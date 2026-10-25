using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class CompositeWinAccessorTests
{
    private const string NativeWindowId = "kwin:f66232b1-8542-48ef-8c53-a8151f0bc453";
    private const string X11WindowId = "0x01e00007";

    [Fact]
    public async Task TryGetWindowsAsync_ConcatenatesBothSources()
    {
        var x11 = new FakeAccessor([new WindowConfig { WindowId = X11WindowId }]);
        var native = new FakeAccessor([new WindowConfig { WindowId = NativeWindowId }]);
        using var sut = new CompositeWinAccessor(x11, native);

        IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync();

        Assert.NotNull(windows);
        Assert.Equal([X11WindowId, NativeWindowId], windows.Select(window => window.WindowId));
    }

    [Fact]
    public async Task TryGetWindowsAsync_KeepsAvailableSourceWhenTheOtherFails()
    {
        var x11 = new FakeAccessor([new WindowConfig { WindowId = X11WindowId }]);
        using var sut = new CompositeWinAccessor(x11, new FakeAccessor(null));

        IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync();

        Assert.NotNull(windows);
        Assert.Equal(X11WindowId, Assert.Single(windows).WindowId);
    }

    [Fact]
    public async Task TryGetWindowsAsync_ReturnsNullWhenBothSourcesFail()
    {
        using var sut = new CompositeWinAccessor(new FakeAccessor(null), new FakeAccessor(null));

        Assert.Null(await sut.TryGetWindowsAsync());
    }

    [Theory]
    [InlineData(NativeWindowId, false, true)]
    [InlineData(X11WindowId, true, false)]
    public async Task Operations_AreRoutedByWindowIdentifier(
        string windowId,
        bool expectX11,
        bool expectNative
    )
    {
        var x11 = new FakeAccessor([]);
        var native = new FakeAccessor([]);
        using var sut = new CompositeWinAccessor(x11, native);

        await sut.TryActivateWindowAsync(windowId);
        await sut.TryRenameWindowAsync(windowId, "title");

        Assert.Equal(expectX11 ? [windowId, windowId] : [], x11.Calls);
        Assert.Equal(expectNative ? [windowId, windowId] : [], native.Calls);
    }

    [Fact]
    public async Task DisposeAsync_DisposesBothAccessors()
    {
        var x11 = new FakeAccessor([]);
        var native = new FakeAccessor([]);
        var sut = new CompositeWinAccessor(x11, native);

        await sut.DisposeAsync();

        Assert.True(x11.Disposed);
        Assert.True(native.Disposed);
    }

    private sealed class FakeAccessor(IReadOnlyCollection<WindowConfig>? windows) : WinAccessorBase
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public override Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(windows);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(windowId);
            return Task.FromResult(true);
        }

        public override Task<bool> TryRenameWindowAsync(
            string windowId,
            string windowTitle,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(windowId);
            return Task.FromResult(true);
        }

        public override void Dispose() => Disposed = true;
    }
}
