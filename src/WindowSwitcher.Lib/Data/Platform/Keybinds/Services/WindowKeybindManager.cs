using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Config-backed manager for window keybind definitions.
/// </summary>
public sealed class WindowKeybindManager : IWindowKeybindManager
{
    private readonly ConfigFileAccessor _configAccessor;

    /// <summary>
    /// Creates a manager using the shared config accessor singleton.
    /// </summary>
    public WindowKeybindManager()
        : this(ConfigFileAccessor.GetInstance()) { }

    internal WindowKeybindManager(ConfigFileAccessor configAccessor)
    {
        ArgumentNullException.ThrowIfNull(configAccessor);
        _configAccessor = configAccessor;
    }

    /// <inheritdoc />
    public event EventHandler? BindingsChanged;

    /// <inheritdoc />
    public IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets()
    {
        return _configAccessor.ReadConfig(config => CloneTargets(config.WindowKeybindTargets));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<WindowKeybindShortcut> GetShortcutsForTarget(string targetId)
    {
        targetId = KeybindCatalogBuilder.NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return [];

        return _configAccessor.ReadConfig(config =>
        {
            KeybindCatalog catalog = BuildCatalog(config.WindowKeybindTargets);
            if (
                !catalog.TryGetTarget(targetId, out WindowKeybindTargetConfig? target)
                || target is null
            )
                return [];

            return target.Shortcuts.Select(KeybindCatalogBuilder.CloneShortcut).ToArray();
        });
    }

    /// <inheritdoc />
    public void UpsertTarget(string targetId, string displayLabel)
    {
        targetId = KeybindCatalogBuilder.NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return;

        string normalizedLabel = KeybindCatalogBuilder.NormalizeDisplayLabel(displayLabel);
        bool changed = false;

        _configAccessor.UpdateConfig(config =>
        {
            NormalizeTargetsInPlace(config);
            WindowKeybindTargetConfig? target = FindTarget(config.WindowKeybindTargets, targetId);
            if (target is null)
            {
                config.WindowKeybindTargets.Add(
                    new WindowKeybindTargetConfig
                    {
                        TargetId = targetId,
                        DisplayLabel = normalizedLabel,
                        Shortcuts = [],
                    }
                );
                changed = true;
                return;
            }

            if (
                !string.IsNullOrWhiteSpace(normalizedLabel)
                && !string.Equals(target.DisplayLabel, normalizedLabel, StringComparison.Ordinal)
            )
            {
                target.DisplayLabel = normalizedLabel;
                changed = true;
            }
        });

        if (changed)
            RaiseBindingsChanged();
    }

    /// <inheritdoc />
    public KeybindShortcutAddResult AddShortcut(
        string targetId,
        string displayLabel,
        KeyCombination combination
    )
    {
        ArgumentNullException.ThrowIfNull(combination);

        targetId = KeybindCatalogBuilder.NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return KeybindShortcutAddResult.Invalid("No keybind target selected.");

        if (!KeyCombinationParser.IsValid(combination))
            return KeybindShortcutAddResult.Invalid("The shortcut is invalid.");

        KeyCombination normalizedCombination = KeyCombinationParser.Normalize(combination);
        string normalizedDisplayLabel = KeybindCatalogBuilder.NormalizeDisplayLabel(displayLabel);

        KeybindShortcutAddResult result = KeybindShortcutAddResult.Invalid("The shortcut is invalid.");

        _configAccessor.UpdateConfig(config =>
        {
            NormalizeTargetsInPlace(config);

            WindowKeybindTargetConfig target = GetOrCreateTarget(
                config.WindowKeybindTargets,
                targetId,
                normalizedDisplayLabel
            );

            if (
                target.Shortcuts.Any(shortcut =>
                    KeyCombinationParser.IsValid(shortcut.Combination)
                    && KeyCombinationParser.Normalize(shortcut.Combination).Equals(normalizedCombination)
                )
            )
            {
                result = KeybindShortcutAddResult.Duplicate(
                    $"Shortcut {KeyCombinationParser.ToCanonicalString(normalizedCombination)} is already assigned to this target."
                );
                return;
            }

            KeybindCatalog catalog = BuildCatalog(config.WindowKeybindTargets);
            WindowKeybindTargetConfig? conflictTarget =
                catalog.TryResolveTarget(normalizedCombination, out string conflictTargetId)
                && !string.Equals(conflictTargetId, targetId, StringComparison.Ordinal)
                && catalog.TryGetTarget(conflictTargetId, out WindowKeybindTargetConfig? resolvedTarget)
                    ? resolvedTarget
                    : null;

            if (conflictTarget is not null)
            {
                string conflictLabel = string.IsNullOrWhiteSpace(conflictTarget.DisplayLabel)
                    ? conflictTarget.TargetId
                    : conflictTarget.DisplayLabel;
                result = KeybindShortcutAddResult.Conflict(
                    $"Conflict: {KeyCombinationParser.ToCanonicalString(normalizedCombination)} is already assigned to \"{conflictLabel}\"."
                );
                return;
            }

            target.Shortcuts.Add(
                new WindowKeybindShortcut
                {
                    Combination = normalizedCombination,
                }
            );
            result = KeybindShortcutAddResult.Added();
        });

        if (result.Status == KeybindRegistrationResult.Added)
            RaiseBindingsChanged();

        return result;
    }

    /// <inheritdoc />
    public bool RemoveShortcut(string targetId, KeyCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);

        targetId = KeybindCatalogBuilder.NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        if (!KeyCombinationParser.IsValid(combination))
            return false;

        KeyCombination normalizedCombination = KeyCombinationParser.Normalize(combination);
        bool removed = false;

        _configAccessor.UpdateConfig(config =>
        {
            NormalizeTargetsInPlace(config);
            WindowKeybindTargetConfig? target = FindTarget(config.WindowKeybindTargets, targetId);
            if (target is null)
                return;

            removed = target.Shortcuts.RemoveAll(shortcut =>
                    KeyCombinationParser.IsValid(shortcut.Combination)
                    && KeyCombinationParser.Normalize(shortcut.Combination)
                        .Equals(normalizedCombination)
                )
                > 0;

            if (target.Shortcuts.Count == 0)
                config.WindowKeybindTargets.Remove(target);
        });

        if (removed)
            RaiseBindingsChanged();

        return removed;
    }

    public bool TryResolveTarget(KeyCombination combination, out string targetId)
    {
        ArgumentNullException.ThrowIfNull(combination);

        string resolvedTargetId = string.Empty;
        bool found = _configAccessor.ReadConfig(config =>
        {
            KeybindCatalog catalog = BuildCatalog(config.WindowKeybindTargets);
            if (!catalog.TryResolveTarget(combination, out string localTargetId))
                return false;

            resolvedTargetId = localTargetId;
            return true;
        });

        targetId = found ? resolvedTargetId : string.Empty;
        return found;
    }

    private static WindowKeybindTargetConfig GetOrCreateTarget(
        List<WindowKeybindTargetConfig> targets,
        string targetId,
        string displayLabel
    )
    {
        WindowKeybindTargetConfig? existing = targets.FirstOrDefault(target =>
            string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
        );
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(displayLabel))
                existing.DisplayLabel = displayLabel;
            return existing;
        }

        var created = new WindowKeybindTargetConfig
        {
            TargetId = targetId,
            DisplayLabel = displayLabel,
            Shortcuts = [],
        };
        targets.Add(created);
        return created;
    }

    private static KeybindCatalog BuildCatalog(IEnumerable<WindowKeybindTargetConfig>? targets)
    {
        return KeybindCatalogBuilder.Build(targets);
    }

    private static WindowKeybindTargetConfig[] CloneTargets(
        IEnumerable<WindowKeybindTargetConfig>? targets)
    {
        return BuildCatalog(targets).Targets.Select(KeybindCatalogBuilder.CloneTarget).ToArray();
    }

    private static void NormalizeTargetsInPlace(ConfigFile config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.WindowKeybindTargets = CloneTargets(config.WindowKeybindTargets).ToList();
    }

    private static WindowKeybindTargetConfig? FindTarget(
        IEnumerable<WindowKeybindTargetConfig> targets,
        string targetId)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(targetId);

        return targets.FirstOrDefault(target =>
            string.Equals(target.TargetId, targetId, StringComparison.Ordinal)
        );
    }

    private void RaiseBindingsChanged()
    {
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
