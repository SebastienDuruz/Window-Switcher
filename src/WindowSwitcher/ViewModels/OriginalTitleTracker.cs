using System;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Decides which original title to remember for a window after it is renamed.
/// </summary>
public static class OriginalTitleTracker
{
    /// <summary>
    /// Returns the original title to keep after renaming a window from
    /// <paramref name="titleBeforeRename"/> to <paramref name="newTitle"/>: the title
    /// remembered from an earlier rename when there is one, otherwise the title before
    /// this rename. Returns an empty string when the window gets its original title
    /// back, since there is nothing left to restore.
    /// </summary>
    public static string Resolve(string titleBeforeRename, string? storedOriginal, string newTitle)
    {
        ArgumentNullException.ThrowIfNull(titleBeforeRename);
        ArgumentNullException.ThrowIfNull(newTitle);

        string original = string.IsNullOrWhiteSpace(storedOriginal)
            ? titleBeforeRename
            : storedOriginal;
        return string.Equals(newTitle, original, StringComparison.Ordinal)
            ? string.Empty
            : original;
    }
}
