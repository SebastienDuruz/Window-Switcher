using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

/// <summary>
/// Produces stable target identifiers from process/title window metadata.
/// </summary>
public static class WindowTargetKeyFactory
{
    /// <summary>
    /// Builds a stable key from a window model.
    /// </summary>
    public static string Create(WindowConfig windowConfig)
    {
        ArgumentNullException.ThrowIfNull(windowConfig);

        return Create(windowConfig.ProcessName, windowConfig.WindowTitle);
    }

    /// <summary>
    /// Builds a stable key from process and title values.
    /// </summary>
    public static string Create(string? processName, string? windowTitle)
    {
        string title = Normalize(windowTitle);
        string process = Normalize(processName);

        if (string.IsNullOrWhiteSpace(process))
            return title;

        return $"{process}|{title}";
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
