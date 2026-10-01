using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Runtime;

internal sealed class KeybindFilterCatalogSnapshot
{
    private static readonly KeybindFilterCatalogSnapshot EmptySnapshot = new(
        new KeybindCatalog(
            [],
            new Dictionary<string, WindowKeybindTargetConfig>(StringComparer.Ordinal),
            new Dictionary<KeyCombination, string>()
        )
    );
    private readonly KeybindCatalog _catalog;

    private KeybindFilterCatalogSnapshot(KeybindCatalog catalog)
    {
        _catalog = catalog;
    }

    public static KeybindFilterCatalogSnapshot Empty => EmptySnapshot;

    public static KeybindFilterCatalogSnapshot Create(
        IReadOnlyCollection<WindowKeybindTargetConfig> targets
    )
    {
        ArgumentNullException.ThrowIfNull(targets);

        return new KeybindFilterCatalogSnapshot(KeybindCatalogBuilder.Build(targets));
    }

    public bool TryResolveTarget(KeyCombination combination, out string targetId)
    {
        return _catalog.TryResolveTarget(combination, out targetId);
    }
}
