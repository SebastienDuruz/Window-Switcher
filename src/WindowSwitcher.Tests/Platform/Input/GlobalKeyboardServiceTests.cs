using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

public sealed class GlobalKeyboardServiceTests
{
    [Fact]
    public async Task StartAndStop_AreIdempotent()
    {
        var listener = new FakeGlobalKeyboardListener();
        await using var sut = new GlobalKeyboardService(listener);

        await sut.StartAsync();
        await sut.StartAsync();
        await sut.StopAsync();
        await sut.StopAsync();

        Assert.Equal(1, listener.StartCalls);
        Assert.Equal(1, listener.StopCalls);
    }

    [Fact]
    public async Task KeyEvent_IsForwardedFromListener()
    {
        var listener = new FakeGlobalKeyboardListener();
        await using var sut = new GlobalKeyboardService(listener);

        GlobalKeyEventArgs? observedEvent = null;
        sut.KeyEvent += (_, keyEvent) => observedEvent = keyEvent;

        listener.Emit(
            new GlobalKeyEventArgs
            {
                Platform = "Windows",
                KeyCode = "VK_41",
                KeyName = "A",
                State = GlobalKeyState.Down,
            }
        );

        Assert.NotNull(observedEvent);
        Assert.Equal("VK_41", observedEvent.KeyCode);
    }

    [Fact]
    public async Task StartAsync_ThrowsAfterDispose()
    {
        var listener = new FakeGlobalKeyboardListener();
        var sut = new GlobalKeyboardService(listener);
        await sut.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.StartAsync());
    }

    private sealed class FakeGlobalKeyboardListener : IGlobalKeyboardListener
    {
        private bool _disposed;

        public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

        public bool IsRunning { get; private set; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
                throw new ObjectDisposedException(nameof(FakeGlobalKeyboardListener));

            if (IsRunning)
                return Task.CompletedTask;

            IsRunning = true;
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning)
                return Task.CompletedTask;

            IsRunning = false;
            StopCalls++;
            return Task.CompletedTask;
        }

        public void Emit(GlobalKeyEventArgs keyEvent)
        {
            ArgumentNullException.ThrowIfNull(keyEvent);

            KeyEvent?.Invoke(this, keyEvent);
        }

        public void Dispose()
        {
            _disposed = true;
            IsRunning = false;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
