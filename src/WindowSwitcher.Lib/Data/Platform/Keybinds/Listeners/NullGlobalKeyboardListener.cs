using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners;

/// <summary>
/// No-op listener used when global keyboard capture is unavailable on the current platform.
/// </summary>
public sealed class NullGlobalKeyboardListener : IGlobalKeyboardListener
{
    private readonly object _syncRoot = new();
    private bool _isDisposed;

    /// <inheritdoc />
#pragma warning disable CS0067
    public event EventHandler<GlobalKeyEventArgs>? KeyEvent;
#pragma warning restore CS0067

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            IsRunning = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_isDisposed)
                return Task.CompletedTask;

            IsRunning = false;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        lock (_syncRoot)
        {
            if (_isDisposed)
                return;

            IsRunning = false;
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(NullGlobalKeyboardListener));
    }
}
