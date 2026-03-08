using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

internal sealed class KeybindCatalog
{
    private readonly IReadOnlyList<WindowKeybindTargetConfig> _targets;
    private readonly IReadOnlyDictionary<string, WindowKeybindTargetConfig> _targetsById;
    private readonly IReadOnlyDictionary<KeyCombination, string> _targetsByCombination;

    public KeybindCatalog(
        IReadOnlyList<WindowKeybindTargetConfig> targets,
        IReadOnlyDictionary<string, WindowKeybindTargetConfig> targetsById,
        IReadOnlyDictionary<KeyCombination, string> targetsByCombination)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(targetsById);
        ArgumentNullException.ThrowIfNull(targetsByCombination);

        _targets = targets;
        _targetsById = targetsById;
        _targetsByCombination = targetsByCombination;
    }

    public IReadOnlyList<WindowKeybindTargetConfig> Targets => _targets;

    public bool TryGetTarget(string targetId, out WindowKeybindTargetConfig? target)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        return _targetsById.TryGetValue(targetId, out target);
    }

    public bool TryResolveTarget(KeyCombination combination, out string targetId)
    {
        ArgumentNullException.ThrowIfNull(combination);

        if (!KeyCombinationParser.IsValid(combination))
        {
            targetId = string.Empty;
            return false;
        }

        return _targetsByCombination.TryGetValue(
            KeyCombinationParser.Normalize(combination),
            out targetId!
        );
    }
}
