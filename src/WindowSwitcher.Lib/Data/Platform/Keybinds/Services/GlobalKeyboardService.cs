using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Facade service exposing a platform-agnostic global keyboard stream.
/// </summary>
public sealed class GlobalKeyboardService : IGlobalKeyboardService
{
    private readonly IGlobalKeyboardListener _listener;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _isDisposed;

    /// <summary>
    /// Creates a new keyboard service.
    /// </summary>
    public GlobalKeyboardService(IGlobalKeyboardListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        _listener = listener;
        _listener.KeyEvent += OnListenerKeyEvent;
    }

    /// <inheritdoc />
    public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    /// <inheritdoc />
    public bool IsRunning => _listener.IsRunning;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_listener.IsRunning)
                return;

            GlobalKeyboardTrace.Info("Starting global keyboard service.");
            await _listener.StartAsync(cancellationToken).ConfigureAwait(false);
            GlobalKeyboardTrace.Info("Global keyboard service started.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GlobalKeyboardTrace.Error("Global keyboard service failed to start.", ex);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_listener.IsRunning)
                return;

            GlobalKeyboardTrace.Info("Stopping global keyboard service.");
            await _listener.StopAsync(cancellationToken).ConfigureAwait(false);
            GlobalKeyboardTrace.Info("Global keyboard service stopped.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GlobalKeyboardTrace.Warning(
                $"Global keyboard service reported an error while stopping: {ex.Message}"
            );
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _listener.KeyEvent -= OnListenerKeyEvent;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Global keyboard service cleanup reported a non-fatal error: {ex.Message}"
            );
        }

        await _listener.DisposeAsync().ConfigureAwait(false);
        KeyEvent = null;
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnListenerKeyEvent(object? sender, GlobalKeyEventArgs eventArgs)
    {
        Delegate[] subscribers = KeyEvent?.GetInvocationList() ?? [];
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((EventHandler<GlobalKeyEventArgs>)subscriber).Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                GlobalKeyboardTrace.Warning(
                    $"Global keyboard service subscriber failed for key {eventArgs.KeyCode}: {ex.Message}"
                );
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GlobalKeyboardService));
    }
}
