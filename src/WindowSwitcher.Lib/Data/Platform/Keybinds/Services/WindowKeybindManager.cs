using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

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
    public IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets()
    {
        return _configAccessor.ReadConfig(config =>
            NormalizeTargets(config.WindowKeybindTargets).Select(CloneTarget).ToArray()
        );
    }

    /// <inheritdoc />
    public IReadOnlyCollection<WindowKeybindBinding> GetBindingsForTarget(string targetId)
    {
        targetId = NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return [];

        return _configAccessor.ReadConfig(config =>
        {
            WindowKeybindTargetConfig? target = NormalizeTargets(config.WindowKeybindTargets)
                .FirstOrDefault(entry => string.Equals(entry.TargetId, targetId, StringComparison.Ordinal));
            if (target is null)
                return [];

            return target.Shortcuts.Select(CloneBinding).ToArray();
        });
    }

    /// <inheritdoc />
    public void UpsertTarget(string targetId, string displayLabel)
    {
        targetId = NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return;

        string normalizedLabel = NormalizeDisplayLabel(displayLabel);

        _configAccessor.UpdateConfig(config =>
        {
            config.WindowKeybindTargets = NormalizeTargets(config.WindowKeybindTargets).ToList();
            WindowKeybindTargetConfig? target = config.WindowKeybindTargets.FirstOrDefault(entry =>
                string.Equals(entry.TargetId, targetId, StringComparison.Ordinal)
            );
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
                return;
            }

            if (!string.IsNullOrWhiteSpace(normalizedLabel))
                target.DisplayLabel = normalizedLabel;
        });
    }

    /// <inheritdoc />
    public KeybindRegistrationResult TryAddBinding(
        string targetId,
        string displayLabel,
        KeyCombination combination,
        out string message
    )
    {
        ArgumentNullException.ThrowIfNull(combination);

        targetId = NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            message = "No keybind target selected.";
            return KeybindRegistrationResult.Invalid;
        }

        if (!KeyCombinationParser.IsValid(combination))
        {
            message = "The shortcut is invalid.";
            return KeybindRegistrationResult.Invalid;
        }

        KeyCombination normalizedCombination = KeyCombinationParser.Normalize(combination);
        string normalizedDisplayLabel = NormalizeDisplayLabel(displayLabel);

        KeybindRegistrationResult result = KeybindRegistrationResult.Invalid;
        string localMessage = "The shortcut is invalid.";

        _configAccessor.UpdateConfig(config =>
        {
            config.WindowKeybindTargets = NormalizeTargets(config.WindowKeybindTargets).ToList();

            WindowKeybindTargetConfig target = GetOrCreateTarget(
                config.WindowKeybindTargets,
                targetId,
                normalizedDisplayLabel
            );

            if (
                target.Shortcuts.Any(binding =>
                    KeyCombinationParser.IsValid(binding.Combination)
                    && KeyCombinationParser.Normalize(binding.Combination).Equals(normalizedCombination)
                )
            )
            {
                result = KeybindRegistrationResult.Duplicate;
                localMessage =
                    $"Shortcut {KeyCombinationParser.ToCanonicalString(normalizedCombination)} is already assigned to this target.";
                return;
            }

            WindowKeybindTargetConfig? conflictTarget = config.WindowKeybindTargets
                .Where(entry => !string.Equals(entry.TargetId, targetId, StringComparison.Ordinal))
                .FirstOrDefault(entry =>
                    entry.Shortcuts.Any(binding =>
                        binding.Enabled
                        && KeyCombinationParser.IsValid(binding.Combination)
                        && KeyCombinationParser.Normalize(binding.Combination)
                            .Equals(normalizedCombination)
                    )
                );

            if (conflictTarget is not null)
            {
                result = KeybindRegistrationResult.Conflict;
                string conflictLabel = string.IsNullOrWhiteSpace(conflictTarget.DisplayLabel)
                    ? conflictTarget.TargetId
                    : conflictTarget.DisplayLabel;
                localMessage =
                    $"Conflict: {KeyCombinationParser.ToCanonicalString(normalizedCombination)} is already assigned to \"{conflictLabel}\".";
                return;
            }

            target.Shortcuts.Add(
                new WindowKeybindBinding
                {
                    Enabled = true,
                    Combination = normalizedCombination,
                }
            );
            result = KeybindRegistrationResult.Added;
            localMessage = string.Empty;
        });

        message = localMessage;
        return result;
    }

    /// <inheritdoc />
    public bool RemoveBinding(string targetId, KeyCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);

        targetId = NormalizeTargetId(targetId);
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        if (!KeyCombinationParser.IsValid(combination))
            return false;

        KeyCombination normalizedCombination = KeyCombinationParser.Normalize(combination);
        bool removed = false;

        _configAccessor.UpdateConfig(config =>
        {
            config.WindowKeybindTargets = NormalizeTargets(config.WindowKeybindTargets).ToList();
            WindowKeybindTargetConfig? target = config.WindowKeybindTargets.FirstOrDefault(entry =>
                string.Equals(entry.TargetId, targetId, StringComparison.Ordinal)
            );
            if (target is null)
                return;

            removed = target.Shortcuts.RemoveAll(binding =>
                    KeyCombinationParser.IsValid(binding.Combination)
                    && KeyCombinationParser.Normalize(binding.Combination)
                        .Equals(normalizedCombination)
                )
                > 0;

            if (target.Shortcuts.Count == 0)
                config.WindowKeybindTargets.Remove(target);
        });

        return removed;
    }

    /// <inheritdoc />
    public bool TryResolveTarget(KeyCombination combination, out string targetId)
    {
        ArgumentNullException.ThrowIfNull(combination);

        string resolvedTargetId = string.Empty;
        if (!KeyCombinationParser.IsValid(combination))
        {
            targetId = string.Empty;
            return false;
        }

        KeyCombination normalizedCombination = KeyCombinationParser.Normalize(combination);
        bool found = _configAccessor.ReadConfig(config =>
        {
            foreach (WindowKeybindTargetConfig target in NormalizeTargets(config.WindowKeybindTargets))
            {
                foreach (WindowKeybindBinding binding in target.Shortcuts)
                {
                    if (!binding.Enabled || !KeyCombinationParser.IsValid(binding.Combination))
                        continue;

                    if (
                        !KeyCombinationParser.Normalize(binding.Combination)
                            .Equals(normalizedCombination)
                    )
                        continue;

                    resolvedTargetId = target.TargetId;
                    return true;
                }
            }

            return false;
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

    private static IEnumerable<WindowKeybindTargetConfig> NormalizeTargets(
        IEnumerable<WindowKeybindTargetConfig>? targets)
    {
        if (targets is null)
            return [];

        return targets
            .Where(target => target is not null)
            .Select(target => NormalizeTarget(target))
            .Where(target => !string.IsNullOrWhiteSpace(target.TargetId));
    }

    private static WindowKeybindTargetConfig NormalizeTarget(WindowKeybindTargetConfig target)
    {
        string targetId = NormalizeTargetId(target.TargetId);
        string displayLabel = NormalizeDisplayLabel(target.DisplayLabel);

        List<WindowKeybindBinding> shortcuts = (target.Shortcuts ?? [])
            .Where(binding =>
                binding is not null
                && binding.Combination is not null
                && KeyCombinationParser.IsValid(binding.Combination)
            )
            .Select(binding => new WindowKeybindBinding
            {
                Enabled = binding.Enabled,
                Combination = KeyCombinationParser.Normalize(binding.Combination),
            })
            .ToList();

        return new WindowKeybindTargetConfig
        {
            TargetId = targetId,
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? targetId : displayLabel,
            Shortcuts = shortcuts,
        };
    }

    private static WindowKeybindTargetConfig CloneTarget(WindowKeybindTargetConfig target)
    {
        return new WindowKeybindTargetConfig
        {
            TargetId = target.TargetId,
            DisplayLabel = target.DisplayLabel,
            Shortcuts = target.Shortcuts.Select(CloneBinding).ToList(),
        };
    }

    private static WindowKeybindBinding CloneBinding(WindowKeybindBinding binding)
    {
        return new WindowKeybindBinding
        {
            Enabled = binding.Enabled,
            Combination = KeyCombinationParser.Normalize(binding.Combination),
        };
    }

    private static string NormalizeTargetId(string? targetId)
    {
        return string.IsNullOrWhiteSpace(targetId) ? string.Empty : targetId.Trim().ToLowerInvariant();
    }

    private static string NormalizeDisplayLabel(string? displayLabel)
    {
        return string.IsNullOrWhiteSpace(displayLabel) ? string.Empty : displayLabel.Trim();
    }
}