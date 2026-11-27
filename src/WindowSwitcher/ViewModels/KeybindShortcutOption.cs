using System;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// One configured shortcut of a keybind target, as shown on the Keybinds page.
/// </summary>
public sealed class KeybindShortcutOption
{
    public KeybindShortcutOption(WindowKeybindShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        Combination = shortcut.Combination.Clone();
        Display = KeyCombinationParser.ToCanonicalString(shortcut.Combination);
    }

    public KeyCombination Combination { get; }

    public string Display { get; }
}
