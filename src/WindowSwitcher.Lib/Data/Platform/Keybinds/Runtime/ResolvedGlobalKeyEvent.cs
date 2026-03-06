using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

internal readonly record struct ResolvedGlobalKeyEvent(
    GlobalKeyState State,
    bool IsRepeat,
    KeybindModifier? Modifier,
    KeybindPrimaryKey PrimaryKey);
