using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Factories.Abstractions;

/// <summary>
/// Factory creating the platform-specific global keyboard listener.
/// </summary>
public interface IGlobalKeyboardListenerFactory
{
    /// <summary>
    /// Creates a listener instance matching the current runtime platform.
    /// </summary>
    IGlobalKeyboardListener Create();
}
