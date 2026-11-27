using System;

namespace WindowSwitcher.Windows;

/// <summary>
/// Reset operations offered by the settings window.
/// </summary>
/// <param name="ResetLayout">Forgets the saved position and size of every preview.</param>
/// <param name="ResetSettings">Restores the default configuration.</param>
/// <param name="ResetAllPreviews">Resets remembered capture selections, or
/// <see langword="null"/> when the capture backend has none.</param>
internal sealed record SettingsResetActions(
    Action ResetLayout,
    Action ResetSettings,
    Action? ResetAllPreviews
);
