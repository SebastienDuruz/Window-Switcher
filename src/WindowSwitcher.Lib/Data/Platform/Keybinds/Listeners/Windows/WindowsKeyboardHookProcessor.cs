using Serilog;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;

/// <summary>
/// Platform-independent part of the <c>WH_KEYBOARD_LL</c> callback: turns raw hook data into key
/// events, tracks auto-repeat and decides whether the key reaches other applications.
/// </summary>
internal sealed class WindowsKeyboardHookProcessor
{
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmKeyUp = 0x0101;
    internal const uint WmSysKeyDown = 0x0104;
    internal const uint WmSysKeyUp = 0x0105;
    internal const uint LlkhfExtended = 0x00000001;
    internal const uint LlkhfInjected = 0x00000010;
    private const int KeyNameExtendedBit = 1 << 24;

    private readonly object _pressedKeysSync = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly Func<int, string?> _resolveKeyName;
    private readonly ILogger _logger;

    /// <param name="resolveKeyName">
    /// Receives the <c>GetKeyNameText</c> lParam: scan code in bits 16-23, extended flag in bit 24.
    /// </param>
    /// <param name="logger">Receives filter failures.</param>
    internal WindowsKeyboardHookProcessor(Func<int, string?> resolveKeyName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(resolveKeyName);
        ArgumentNullException.ThrowIfNull(logger);
        _resolveKeyName = resolveKeyName;
        _logger = logger;
    }

    /// <summary>
    /// Creates the key event for a hook notification, or returns <see langword="null"/> when the
    /// notification must be passed on untouched: not a key message, or a key injected by software
    /// (including the keys this application sends), which would otherwise loop back.
    /// </summary>
    internal GlobalKeyEventArgs? TryCreateEvent(
        uint message,
        uint virtualKeyCode,
        uint scanCode,
        uint flags
    )
    {
        bool isDown = message is WmKeyDown or WmSysKeyDown;
        bool isUp = message is WmKeyUp or WmSysKeyUp;
        if (!isDown && !isUp)
            return null;
        if ((flags & LlkhfInjected) != 0)
            return null;

        bool isRepeat = false;
        lock (_pressedKeysSync)
        {
            if (isDown)
                isRepeat = !_pressedKeys.Add(virtualKeyCode);
            else
                _pressedKeys.Remove(virtualKeyCode);
        }

        bool isExtended = (flags & LlkhfExtended) != 0;
        int keyNameParameter =
            unchecked((int)((scanCode & 0xFF) << 16)) | (isExtended ? KeyNameExtendedBit : 0);

        return GlobalKeyboardEventMapper.FromWindows(
            virtualKeyCode,
            _resolveKeyName(keyNameParameter),
            isDown ? GlobalKeyState.Down : GlobalKeyState.Up,
            isRepeat,
            isExtended
        );
    }

    /// <summary>
    /// Returns whether the key must be swallowed. A failing filter never blocks the keyboard.
    /// </summary>
    internal bool ShouldConsume(GlobalKeyEventArgs keyEvent, IKeyboardInputFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        if (filter is null)
            return false;

        try
        {
            return filter.ProcessEvent(keyEvent).Routing == KeyboardEventRouting.Consume;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Windows keyboard filter failed");
            return false;
        }
    }

    /// <summary>
    /// Forgets held keys, so the first press after a restart is not reported as a repeat.
    /// </summary>
    internal void Reset()
    {
        lock (_pressedKeysSync)
            _pressedKeys.Clear();
    }
}
