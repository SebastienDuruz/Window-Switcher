using System;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.ViewModels;

public sealed record KeybindCaptureResult(
    bool Succeeded,
    KeyCombination? Combination,
    string Message
)
{
    public static KeybindCaptureResult Success(KeyCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);

        return new KeybindCaptureResult(true, combination, string.Empty);
    }

    public static KeybindCaptureResult Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new KeybindCaptureResult(false, null, message);
    }
}
