using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Runtime bridge from global key events to configured window activation actions.
/// </summary>
public sealed class GlobalWindowKeybindRuntimeService
    : IGlobalWindowKeybindRuntimeService, IKeyboardInputFilter
{
    private readonly object _syncRoot = new();
    private readonly IGlobalKeyboardListener _globalKeyboardListener;
    private readonly IWindowKeybindManager _keybindManager;
    private readonly IWindowKeybindActivator _activator;
    private readonly HashSet<KeybindModifier> _pressedModifiers = [];
    private readonly HashSet<KeybindModifier> _bufferedModifiers = [];
    private readonly HashSet<KeybindModifier> _consumedModifiers = [];
    private readonly HashSet<KeybindPrimaryKey> _pressedPrimaryKeys = [];
    private readonly HashSet<KeybindPrimaryKey> _consumedPrimaryKeys = [];
    private KeybindFilterCatalogSnapshot _catalogSnapshot = KeybindFilterCatalogSnapshot.Empty;
    private bool _isDisposed;

    /// <summary>
    /// Creates a runtime keybind service.
    /// </summary>
    public GlobalWindowKeybindRuntimeService(
        IGlobalKeyboardListener globalKeyboardListener,
        IWindowKeybindManager keybindManager,
        IWindowKeybindActivator activator)
    {
        ArgumentNullException.ThrowIfNull(globalKeyboardListener);
        ArgumentNullException.ThrowIfNull(keybindManager);
        ArgumentNullException.ThrowIfNull(activator);

        _globalKeyboardListener = globalKeyboardListener;
        _keybindManager = keybindManager;
        _activator = activator;
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

            if (ReferenceEquals(_globalKeyboardListener.InputFilter, this))
                _globalKeyboardListener.InputFilter = null;

            ResetStateUnsafe();
            IsRunning = false;
        }

        GlobalKeyboardTrace.Info("Global window keybind runtime service stopped.");
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

            if (!GlobalKeyEventKeyResolver.TryResolve(keyEvent, out ResolvedGlobalKeyEvent resolvedEvent))
            {
                if (_consumedModifiers.Count > 0)
                    return KeyboardFilterDecision.Consume();

                if (_bufferedModifiers.Count > 0)
                {
                    ClearBufferedStateUnsafe();
                    return KeyboardFilterDecision.Forward(
                        flushBufferedEvents: true,
                        forwardCurrentEventViaForwarder: true
                    );
                }

                return KeyboardFilterDecision.Forward();
            }

            return resolvedEvent.Modifier.HasValue
                ? ProcessModifierEventUnsafe(resolvedEvent.Modifier.Value, resolvedEvent)
                : ProcessPrimaryEventUnsafe(resolvedEvent.PrimaryKey, resolvedEvent);
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
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Global window keybind runtime shutdown reported a non-fatal error: {ex.Message}"
            );
        }

        GC.SuppressFinalize(this);
    }

    private KeyboardFilterDecision ProcessModifierEventUnsafe(
        KeybindModifier modifier,
        ResolvedGlobalKeyEvent resolvedEvent)
    {
        ApplyModifierStateUnsafe(modifier, resolvedEvent.State);

        if (_consumedModifiers.Contains(modifier))
        {
            if (resolvedEvent.State == GlobalKeyState.Up)
                _consumedModifiers.Remove(modifier);

            return KeyboardFilterDecision.Consume();
        }

        if (_consumedModifiers.Count > 0)
        {
            if (resolvedEvent.State == GlobalKeyState.Down)
                _consumedModifiers.Add(modifier);

            return KeyboardFilterDecision.Consume();
        }

        if (resolvedEvent.IsRepeat)
            return _bufferedModifiers.Contains(modifier)
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();

        if (resolvedEvent.State == GlobalKeyState.Up)
        {
            if (_bufferedModifiers.Count > 0)
            {
                ClearBufferedStateUnsafe();
                return KeyboardFilterDecision.Forward(
                    flushBufferedEvents: true,
                    forwardCurrentEventViaForwarder: true
                );
            }

            return KeyboardFilterDecision.Forward();
        }

        if (_catalogSnapshot.HasPotentialMatch(_pressedModifiers))
        {
            _bufferedModifiers.Add(modifier);
            return KeyboardFilterDecision.Buffer();
        }

        if (_bufferedModifiers.Count > 0)
        {
            ClearBufferedStateUnsafe();
            return KeyboardFilterDecision.Forward(
                flushBufferedEvents: true,
                forwardCurrentEventViaForwarder: true
            );
        }

        return KeyboardFilterDecision.Forward();
    }

    private KeyboardFilterDecision ProcessPrimaryEventUnsafe(
        KeybindPrimaryKey primaryKey,
        ResolvedGlobalKeyEvent resolvedEvent)
    {
        if (primaryKey == KeybindPrimaryKey.None)
        {
            if (_bufferedModifiers.Count > 0)
            {
                ClearBufferedStateUnsafe();
                return KeyboardFilterDecision.Forward(
                    flushBufferedEvents: true,
                    forwardCurrentEventViaForwarder: true
                );
            }

            return _consumedModifiers.Count > 0
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();
        }

        ApplyPrimaryStateUnsafe(primaryKey, resolvedEvent.State, resolvedEvent.IsRepeat);

        if (_consumedPrimaryKeys.Contains(primaryKey))
        {
            if (resolvedEvent.State == GlobalKeyState.Up)
                _consumedPrimaryKeys.Remove(primaryKey);

            return KeyboardFilterDecision.Consume();
        }

        if (resolvedEvent.State == GlobalKeyState.Up)
        {
            if (_bufferedModifiers.Count > 0)
            {
                ClearBufferedStateUnsafe();
                return KeyboardFilterDecision.Forward(
                    flushBufferedEvents: true,
                    forwardCurrentEventViaForwarder: true
                );
            }

            return _consumedModifiers.Count > 0
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();
        }

        if (resolvedEvent.IsRepeat)
            return _consumedModifiers.Count > 0
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();

        KeyCombination combination = BuildCombination(primaryKey);
        if (_catalogSnapshot.TryResolveTarget(combination, out string targetId))
        {
            foreach (KeybindModifier modifier in EnumerateCombinationModifiers(combination))
                _consumedModifiers.Add(modifier);

            _consumedPrimaryKeys.Add(primaryKey);
            bool hadBufferedModifiers = _bufferedModifiers.Count > 0;
            ClearBufferedStateUnsafe();

            _ = ActivateTargetAsync(targetId);

            return KeyboardFilterDecision.Consume(
                discardBufferedEvents: hadBufferedModifiers,
                matchedCombination: combination,
                matchedTargetId: targetId
            );
        }

        if (_consumedModifiers.Count > 0)
        {
            _consumedPrimaryKeys.Add(primaryKey);
            return KeyboardFilterDecision.Consume();
        }

        if (_bufferedModifiers.Count > 0)
        {
            ClearBufferedStateUnsafe();
            return KeyboardFilterDecision.Forward(
                flushBufferedEvents: true,
                forwardCurrentEventViaForwarder: true
            );
        }

        return KeyboardFilterDecision.Forward();
    }

    private async Task ActivateTargetAsync(string targetId)
    {
        try
        {
            _ = await _activator.TryActivateTargetAsync(targetId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning($"Runtime keybind activation failed: {ex.Message}");
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

    private IEnumerable<KeybindModifier> EnumerateCombinationModifiers(KeyCombination combination)
    {
        if (combination.Ctrl)
            yield return KeybindModifier.Ctrl;

        if (combination.Alt)
            yield return KeybindModifier.Alt;

        if (combination.Shift)
            yield return KeybindModifier.Shift;

        if (combination.Meta)
            yield return KeybindModifier.Meta;
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

    private void ApplyPrimaryStateUnsafe(
        KeybindPrimaryKey primaryKey,
        GlobalKeyState state,
        bool isRepeat)
    {
        if (state == GlobalKeyState.Up)
        {
            _pressedPrimaryKeys.Remove(primaryKey);
            return;
        }

        if (!isRepeat)
            _pressedPrimaryKeys.Add(primaryKey);
    }

    private void ResetStateUnsafe()
    {
        _pressedModifiers.Clear();
        _bufferedModifiers.Clear();
        _consumedModifiers.Clear();
        _pressedPrimaryKeys.Clear();
        _consumedPrimaryKeys.Clear();
    }

    private void ClearBufferedStateUnsafe()
    {
        _bufferedModifiers.Clear();
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
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning($"Failed to refresh keybind snapshot: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GlobalWindowKeybindRuntimeService));
    }
}
