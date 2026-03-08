using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;

/// <summary>
/// Builds grouped keybind target lists for the settings UI.
/// </summary>
public interface IWindowKeybindTargetCatalogService
{
    /// <summary>
    /// Returns built-in and client targets for the provided runtime windows.
    /// </summary>
    KeybindTargetCatalogSnapshot GetTargets(IReadOnlyCollection<WindowConfig> runtimeWindows);
}
