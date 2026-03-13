using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

internal sealed class KeybindFilterCatalogSnapshot
{
    private static readonly KeybindFilterCatalogSnapshot EmptySnapshot = new(
        [],
        new KeybindCatalog(
            [],
            new Dictionary<string, WindowKeybindTargetConfig>(StringComparer.Ordinal),
            new Dictionary<KeyCombination, string>()
        )
    );
    private readonly IReadOnlyList<KeyCombination> _combinations;
    private readonly KeybindCatalog _catalog;

    private KeybindFilterCatalogSnapshot(
        IReadOnlyList<KeyCombination> combinations,
        KeybindCatalog catalog)
    {
        _combinations = combinations;
        _catalog = catalog;
    }

    public static KeybindFilterCatalogSnapshot Empty => EmptySnapshot;

    public static KeybindFilterCatalogSnapshot Create(
        IReadOnlyCollection<WindowKeybindTargetConfig> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        KeybindCatalog catalog = KeybindCatalogBuilder.Build(targets);
        KeyCombination[] combinations = catalog
            .Targets.SelectMany(target => target.Shortcuts)
            .Select(shortcut => shortcut.Combination)
            .Where(KeyCombinationParser.IsValid)
            .Select(KeyCombinationParser.Normalize)
            .Distinct()
            .ToArray();

        return new KeybindFilterCatalogSnapshot(combinations, catalog);
    }

    public bool TryResolveTarget(KeyCombination combination, out string targetId)
    {
        return _catalog.TryResolveTarget(combination, out targetId);
    }

    public bool HasPotentialMatch(IReadOnlyCollection<KeybindModifier> pressedModifiers)
    {
        ArgumentNullException.ThrowIfNull(pressedModifiers);

        if (pressedModifiers.Count == 0)
            return false;

        foreach (KeyCombination combination in _combinations)
        {
            if (combination.Key == KeybindPrimaryKey.None)
                continue;

            bool isSubset =
                (!pressedModifiers.Contains(KeybindModifier.Ctrl) || combination.Ctrl)
                && (!pressedModifiers.Contains(KeybindModifier.Alt) || combination.Alt)
                && (!pressedModifiers.Contains(KeybindModifier.Shift) || combination.Shift)
                && (!pressedModifiers.Contains(KeybindModifier.Meta) || combination.Meta);

            if (isSubset)
                return true;
        }

        return false;
    }
}
