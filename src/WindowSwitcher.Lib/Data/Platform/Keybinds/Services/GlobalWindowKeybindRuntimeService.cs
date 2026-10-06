using Serilog;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Runtime bridge from global key events to configured window activation actions.
/// </summary>
public sealed class GlobalWindowKeybindRuntimeService
    : IGlobalWindowKeybindRuntimeService,
        IKeyboardInputFilter
{
    private readonly object _syncRoot = new();
    private readonly IGlobalKeyboardListener _globalKeyboardListener;
    private readonly IWindowKeybindManager _keybindManager;
    private readonly IWindowKeybindActivator _activator;
    private readonly ILogger _logger;
    private readonly HashSet<KeybindModifier> _pressedModifiers = [];
    private readonly HashSet<KeybindPrimaryKey> _consumedPrimaryKeys = [];
    private KeybindFilterCatalogSnapshot _catalogSnapshot = KeybindFilterCatalogSnapshot.Empty;
    private bool _isDisposed;

    /// <summary>
    /// Creates a runtime keybind service.
    /// </summary>
    public GlobalWindowKeybindRuntimeService(
        IGlobalKeyboardListener globalKeyboardListener,
        IWindowKeybindManager keybindManager,
        IWindowKeybindActivator activator
    )
        : this(
            globalKeyboardListener,
            keybindManager,
            activator,
            Log.ForContext<GlobalWindowKeybindRuntimeService>()
        ) { }

    internal GlobalWindowKeybindRuntimeService(
        IGlobalKeyboardListener globalKeyboardListener,
        IWindowKeybindManager keybindManager,
        IWindowKeybindActivator activator,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(globalKeyboardListener);
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(logger);

        _globalKeyboardListener = globalKeyboardListener;
        _keybindManager = keybindManager;
        _activator = activator;
        _logger = logger;
        _keybindManager.BindingsChanged += OnBindingsChanged;
        RefreshCatalogSnapshot();
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

            ResetStateUnsafe();
            RefreshCatalogSnapshotUnsafe();
            _globalKeyboardListener.InputFilter = this;
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
            if (!IsRunning)
                return Task.CompletedTask;

            if (ReferenceEquals(_globalKeyboardListener.InputFilter, this))
                _globalKeyboardListener.InputFilter = null;

            ResetStateUnsafe();
            IsRunning = false;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);

        lock (_syncRoot)
        {
            if (!IsRunning)
                return KeyboardFilterDecision.Forward();

            if (
                !GlobalKeyEventKeyResolver.TryResolve(
                    keyEvent,
                    out ResolvedGlobalKeyEvent resolvedEvent
                )
            )
                return KeyboardFilterDecision.Forward();

            if (resolvedEvent.Modifier.HasValue)
            {
                // Modifiers are always forwarded so the system never sees them stuck or delayed.
                ApplyModifierStateUnsafe(resolvedEvent.Modifier.Value, resolvedEvent.State);
                return KeyboardFilterDecision.Forward();
            }

            return ProcessPrimaryEventUnsafe(resolvedEvent.PrimaryKey, resolvedEvent);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_syncRoot)
        {
            ResetStateUnsafe();
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
        _keybindManager.BindingsChanged -= OnBindingsChanged;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Best-effort shutdown: disposal must complete even if detaching the filter failed.
            _logger.Debug(exception, "Window keybind runtime shutdown failed during disposal");
        }

        GC.SuppressFinalize(this);
    }

    private KeyboardFilterDecision ProcessPrimaryEventUnsafe(
        KeybindPrimaryKey primaryKey,
        ResolvedGlobalKeyEvent resolvedEvent
    )
    {
        if (primaryKey == KeybindPrimaryKey.None)
            return KeyboardFilterDecision.Forward();

        // Only the primary key of a matched shortcut is swallowed, from its press to its release.
        if (resolvedEvent.State == GlobalKeyState.Up)
        {
            return _consumedPrimaryKeys.Remove(primaryKey)
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();
        }

        if (resolvedEvent.IsRepeat)
        {
            return _consumedPrimaryKeys.Contains(primaryKey)
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();
        }

        KeyCombination combination = BuildCombination(primaryKey);
        if (!_catalogSnapshot.TryResolveTarget(combination, out string targetId))
            return KeyboardFilterDecision.Forward();

        _consumedPrimaryKeys.Add(primaryKey);
        _ = ActivateTargetAsync(targetId);

        return KeyboardFilterDecision.Consume(
            matchedCombination: combination,
            matchedTargetId: targetId
        );
    }

    private async Task ActivateTargetAsync(string targetId)
    {
        try
        {
            _ = await _activator.TryActivateTargetAsync(targetId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The target id is not logged: client targets can be derived from window titles.
            _logger.Error(exception, "Activating a keybind target failed");
        }
    }

    private KeyCombination BuildCombination(KeybindPrimaryKey primaryKey)
    {
        return KeyCombinationParser.Normalize(
            new KeyCombination
            {
                Ctrl = _pressedModifiers.Contains(KeybindModifier.Ctrl),
                Alt = _pressedModifiers.Contains(KeybindModifier.Alt),
                Shift = _pressedModifiers.Contains(KeybindModifier.Shift),
                Meta = _pressedModifiers.Contains(KeybindModifier.Meta),
                Key = primaryKey,
            }
        );
    }

    private void ApplyModifierStateUnsafe(KeybindModifier modifier, GlobalKeyState state)
    {
        if (state == GlobalKeyState.Down)
        {
            _pressedModifiers.Add(modifier);
            return;
        }

        _pressedModifiers.Remove(modifier);
    }

    private void ResetStateUnsafe()
    {
        _pressedModifiers.Clear();
        _consumedPrimaryKeys.Clear();
    }

    private void RefreshCatalogSnapshot()
    {
        lock (_syncRoot)
        {
            RefreshCatalogSnapshotUnsafe();
        }
    }

    private void RefreshCatalogSnapshotUnsafe()
    {
        _catalogSnapshot = KeybindFilterCatalogSnapshot.Create(_keybindManager.GetTargets());
    }

    private void OnBindingsChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            RefreshCatalogSnapshot();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Refreshing the keybind catalog after a change failed");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GlobalWindowKeybindRuntimeService));
    }
}
