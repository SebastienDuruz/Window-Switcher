using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Runtime bridge from global key events to configured window activation actions.
/// </summary>
public sealed class GlobalWindowKeybindRuntimeService : IGlobalWindowKeybindRuntimeService
{
    private readonly object _syncRoot = new();
    private readonly IGlobalKeyboardService _globalKeyboardService;
    private readonly IWindowKeybindManager _keybindManager;
    private readonly IWindowKeybindActivator _activator;
    private readonly KeybindPressedStateTracker _pressedStateTracker = new();
    private bool _isDisposed;

    /// <summary>
    /// Creates a runtime keybind service.
    /// </summary>
    public GlobalWindowKeybindRuntimeService(
        IGlobalKeyboardService globalKeyboardService,
        IWindowKeybindManager keybindManager,
        IWindowKeybindActivator activator
    )
    {
        ArgumentNullException.ThrowIfNull(globalKeyboardService);
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(activator);

        _globalKeyboardService = globalKeyboardService;
        _keybindManager = keybindManager;
        _activator = activator;
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (IsRunning)
                return Task.CompletedTask;

            _globalKeyboardService.KeyEvent += OnGlobalKeyEvent;
            IsRunning = true;
        }

        GlobalKeyboardTrace.Info("Global window keybind runtime service started.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!IsRunning)
                return Task.CompletedTask;

            _globalKeyboardService.KeyEvent -= OnGlobalKeyEvent;
            IsRunning = false;
        }

        _pressedStateTracker.Reset();
        GlobalKeyboardTrace.Info("Global window keybind runtime service stopped.");
        return Task.CompletedTask;
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

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Global window keybind runtime shutdown reported a non-fatal error: {ex.Message}"
            );
        }

        GC.SuppressFinalize(this);
    }

    private void OnGlobalKeyEvent(object? sender, GlobalKeyEventArgs keyEvent)
    {
        try
        {
            if (!_pressedStateTracker.TryGetTriggeredCombination(keyEvent, out KeyCombination combination))
                return;

            if (!_keybindManager.TryResolveTarget(combination, out string targetId))
                return;

            _ = _activator.TryActivateTarget(targetId);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning($"Runtime keybind matching failed: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GlobalWindowKeybindRuntimeService));
    }
}
