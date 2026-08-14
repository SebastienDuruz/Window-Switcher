namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Grouped target lists exposed to the keybind settings UI.
/// </summary>
public sealed class KeybindTargetCatalogSnapshot
{
    /// <summary>
    /// Creates a grouped target snapshot.
    /// </summary>
    public KeybindTargetCatalogSnapshot(
        IReadOnlyList<KeybindTargetDescriptor> actionTargets,
        IReadOnlyList<KeybindTargetDescriptor> clientTargets
    )
    {
        ArgumentNullException.ThrowIfNull(actionTargets);
        ArgumentNullException.ThrowIfNull(clientTargets);

        ActionTargets = actionTargets;
        ClientTargets = clientTargets;
    }

    /// <summary>
    /// Gets built-in action targets.
    /// </summary>
    public IReadOnlyList<KeybindTargetDescriptor> ActionTargets { get; }

    /// <summary>
    /// Gets runtime and persisted window targets.
    /// </summary>
    public IReadOnlyList<KeybindTargetDescriptor> ClientTargets { get; }
}
