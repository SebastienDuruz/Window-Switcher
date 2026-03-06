using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

public sealed class NullGlobalKeyboardListenerTests
{
    [Fact]
    public async Task StartAndStop_ToggleRunningState()
    {
        await using var sut = new NullGlobalKeyboardListener();

        await sut.StartAsync();
        Assert.True(sut.IsRunning);

        await sut.StartAsync();
        Assert.True(sut.IsRunning);

        await sut.StopAsync();
        Assert.False(sut.IsRunning);

        await sut.StopAsync();
        Assert.False(sut.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ThrowsAfterDispose()
    {
        var sut = new NullGlobalKeyboardListener();
        await sut.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.StartAsync());
    }
}
