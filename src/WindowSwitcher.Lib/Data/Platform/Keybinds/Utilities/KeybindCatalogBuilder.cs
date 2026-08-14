using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

internal static class KeybindCatalogBuilder
{
    public static KeybindCatalog Build(IEnumerable<WindowKeybindTargetConfig>? targets)
    {
        if (targets is null)
        {
            return new KeybindCatalog(
                [],
                new Dictionary<string, WindowKeybindTargetConfig>(StringComparer.Ordinal),
                new Dictionary<KeyCombination, string>()
            );
        }

        List<WindowKeybindTargetConfig> normalizedTargets = [];
        Dictionary<string, WindowKeybindTargetConfig> targetsById = new(StringComparer.Ordinal);
        Dictionary<KeyCombination, string> targetsByCombination = [];

        foreach (WindowKeybindTargetConfig target in targets.Where(target => target is not null))
        {
            WindowKeybindTargetConfig normalizedTarget = NormalizeTarget(target);
            if (string.IsNullOrWhiteSpace(normalizedTarget.TargetId))
                continue;

            normalizedTargets.Add(normalizedTarget);

            targetsById.TryAdd(normalizedTarget.TargetId, normalizedTarget);

            foreach (WindowKeybindShortcut shortcut in normalizedTarget.Shortcuts)
                targetsByCombination.TryAdd(
                    KeyCombinationParser.Normalize(shortcut.Combination),
                    normalizedTarget.TargetId
                );
        }

        return new KeybindCatalog(normalizedTargets, targetsById, targetsByCombination);
    }

    public static WindowKeybindTargetConfig CloneTarget(WindowKeybindTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return new WindowKeybindTargetConfig
        {
            TargetId = target.TargetId,
            DisplayLabel = target.DisplayLabel,
            Shortcuts = target.Shortcuts.Select(CloneShortcut).ToList(),
        };
    }

    public static WindowKeybindShortcut CloneShortcut(WindowKeybindShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        return new WindowKeybindShortcut
        {
            Combination = KeyCombinationParser.Normalize(shortcut.Combination),
        };
    }

    public static string NormalizeTargetId(string? targetId)
    {
        return string.IsNullOrWhiteSpace(targetId)
            ? string.Empty
            : targetId.Trim().ToLowerInvariant();
    }

    public static string NormalizeDisplayLabel(string? displayLabel)
    {
        return string.IsNullOrWhiteSpace(displayLabel) ? string.Empty : displayLabel.Trim();
    }

    private static WindowKeybindTargetConfig NormalizeTarget(WindowKeybindTargetConfig target)
    {
        string targetId = NormalizeTargetId(target.TargetId);
        string displayLabel = NormalizeDisplayLabel(target.DisplayLabel);

        List<WindowKeybindShortcut> shortcuts = (target.Shortcuts ?? [])
            .Where(shortcut =>
                shortcut is not null
                && shortcut.Combination is not null
                && KeyCombinationParser.IsValid(shortcut.Combination)
            )
            .Select(CloneShortcut)
            .ToList();

        return new WindowKeybindTargetConfig
        {
            TargetId = targetId,
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? targetId : displayLabel,
            Shortcuts = shortcuts,
        };
    }
}
