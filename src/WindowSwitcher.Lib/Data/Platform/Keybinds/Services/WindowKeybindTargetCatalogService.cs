using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Builds the grouped target catalog shown in the keybind settings UI.
/// </summary>
public sealed class WindowKeybindTargetCatalogService : IWindowKeybindTargetCatalogService
{
    private readonly IWindowKeybindManager _keybindManager;

    /// <summary>
    /// Creates a keybind target catalog service.
    /// </summary>
    public WindowKeybindTargetCatalogService(IWindowKeybindManager keybindManager)
    {
        ArgumentNullException.ThrowIfNull(keybindManager);
        _keybindManager = keybindManager;
    }

    /// <inheritdoc />
    public KeybindTargetCatalogSnapshot GetTargets(IReadOnlyCollection<WindowConfig> runtimeWindows)
    {
        ArgumentNullException.ThrowIfNull(runtimeWindows);

        IReadOnlyCollection<WindowKeybindTargetConfig> persistedTargets = _keybindManager.GetTargets();
        IReadOnlyList<KeybindTargetDescriptor> actionTargets = BuildActionTargets(persistedTargets);
        IReadOnlyList<KeybindTargetDescriptor> clientTargets = BuildClientTargets(
            persistedTargets,
            runtimeWindows
        );

        return new KeybindTargetCatalogSnapshot(actionTargets, clientTargets);
    }

    private static string BuildDisplayName(WindowConfig window)
    {
        string title = string.IsNullOrWhiteSpace(window.WindowTitle)
            ? "(untitled)"
            : window.WindowTitle.Trim();
        string process = string.IsNullOrWhiteSpace(window.ProcessName)
            ? "unknown"
            : window.ProcessName.Trim();

        return $"{title} ({process})";
    }

    private static IReadOnlyList<KeybindTargetDescriptor> BuildActionTargets(
        IEnumerable<WindowKeybindTargetConfig> persistedTargets)
    {
        ArgumentNullException.ThrowIfNull(persistedTargets);

        var actionsById = new Dictionary<string, KeybindTargetDescriptor>(StringComparer.Ordinal)
        {
            [KeybindBuiltInTargets.NextClientTargetId] = new KeybindTargetDescriptor(
                KeybindBuiltInTargets.NextClientTargetId,
                KeybindBuiltInTargets.NextClientDisplayName,
                isBuiltIn: true
            ),
            [KeybindBuiltInTargets.PreviousClientTargetId] = new KeybindTargetDescriptor(
                KeybindBuiltInTargets.PreviousClientTargetId,
                KeybindBuiltInTargets.PreviousClientDisplayName,
                isBuiltIn: true
            ),
            [KeybindBuiltInTargets.FocusActiveClientTargetId] = new KeybindTargetDescriptor(
                KeybindBuiltInTargets.FocusActiveClientTargetId,
                KeybindBuiltInTargets.FocusActiveClientDisplayName,
                isBuiltIn: true
            ),
        };

        foreach (WindowKeybindTargetConfig target in persistedTargets)
        {
            if (!KeybindBuiltInTargets.IsBuiltInTarget(target.TargetId))
                continue;

            if (
                actionsById.TryGetValue(target.TargetId, out _)
                && !string.IsNullOrWhiteSpace(target.DisplayLabel)
            )
            {
                actionsById[target.TargetId] = new KeybindTargetDescriptor(
                    target.TargetId,
                    target.DisplayLabel,
                    isBuiltIn: true
                );
                continue;
            }

            if (!actionsById.ContainsKey(target.TargetId))
            {
                actionsById[target.TargetId] = new KeybindTargetDescriptor(
                    target.TargetId,
                    target.DisplayLabel,
                    isBuiltIn: true
                );
            }
        }

        return actionsById.Values.ToArray();
    }

    private static IReadOnlyList<KeybindTargetDescriptor> BuildClientTargets(
        IReadOnlyCollection<WindowKeybindTargetConfig> persistedTargets,
        IReadOnlyCollection<WindowConfig> runtimeWindows)
    {
        ArgumentNullException.ThrowIfNull(persistedTargets);
        ArgumentNullException.ThrowIfNull(runtimeWindows);

        List<KeybindTargetDescriptor> runtimeTargets = runtimeWindows
            .Select(runtimeWindow =>
            {
                string targetId = WindowTargetKeyFactory.Create(runtimeWindow);
                if (string.IsNullOrWhiteSpace(targetId))
                    return null;

                return new KeybindTargetDescriptor(
                    targetId,
                    BuildDisplayName(runtimeWindow),
                    isBuiltIn: false
                );
            })
            .Where(target => target is not null)
            .Select(target => target!)
            .OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> knownTargetIds = runtimeTargets
            .Select(target => target.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (WindowKeybindTargetConfig target in persistedTargets)
        {
            if (string.IsNullOrWhiteSpace(target.TargetId))
                continue;
            if (KeybindBuiltInTargets.IsBuiltInTarget(target.TargetId))
                continue;
            if (!knownTargetIds.Add(target.TargetId))
                continue;

            runtimeTargets.Add(
                new KeybindTargetDescriptor(target.TargetId, target.DisplayLabel, isBuiltIn: false)
            );
        }

        return runtimeTargets;
    }
}
