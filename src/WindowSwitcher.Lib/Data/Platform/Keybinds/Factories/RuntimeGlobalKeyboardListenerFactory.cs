using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Factories;

/// <summary>
/// Resolves the runtime implementation for global keyboard listening.
/// </summary>
public sealed class RuntimeGlobalKeyboardListenerFactory : IGlobalKeyboardListenerFactory
{
    /// <inheritdoc />
    public IGlobalKeyboardListener Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsGlobalKeyboardListener();

        if (OperatingSystem.IsLinux())
            return new LinuxGlobalKeyboardListener();

        GlobalKeyboardTrace.Warning(
            $"Global keyboard listener is not implemented for OS '{System.Runtime.InteropServices.RuntimeInformation.OSDescription}'. Falling back to no-op listener."
        );
        return new NullGlobalKeyboardListener();
    }
}
