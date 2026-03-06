using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Services;

/// <summary>
/// Activates runtime windows from a configured target id.
/// </summary>
public sealed class WindowKeybindActivator : IWindowKeybindActivator
{
    private readonly object _syncRoot = new();
    private readonly WinAccessorBase _accessor;
    private readonly Func<IReadOnlyCollection<WindowConfig>, IReadOnlyList<WindowConfig>> _cycleCandidatesResolver;
    private string _lastActivatedClientId = string.Empty;

    /// <inheritdoc />
    public event EventHandler<string>? WindowActivated;

    /// <summary>
    /// Creates an activator from the current runtime accessor factory.
    /// </summary>
    public WindowKeybindActivator()
        : this(AccessorFactory.GetAccessor(), ResolveSelectedCycleCandidates) { }

    internal WindowKeybindActivator(WinAccessorBase accessor)
        : this(accessor, ResolveSelectedCycleCandidates) { }

    internal WindowKeybindActivator(
        WinAccessorBase accessor,
        Func<IReadOnlyCollection<WindowConfig>, IReadOnlyList<WindowConfig>> cycleCandidatesResolver
    )
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(cycleCandidatesResolver);

        _accessor = accessor;
        _cycleCandidatesResolver = cycleCandidatesResolver;
    }

    /// <inheritdoc />
    public bool TryActivateTarget(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        string normalizedTargetId = targetId.Trim().ToLowerInvariant();
        if (
            string.Equals(
                normalizedTargetId,
                KeybindBuiltInTargets.NextClientTargetId,
                StringComparison.Ordinal
            )
        )
            return TryActivateRelativeClient(step: 1);

        if (
            string.Equals(
                normalizedTargetId,
                KeybindBuiltInTargets.PreviousClientTargetId,
                StringComparison.Ordinal
            )
        )
            return TryActivateRelativeClient(step: -1);

        IReadOnlyCollection<WindowConfig> windows = _accessor.GetWindows();

        WindowConfig? matchingWindow = windows.FirstOrDefault(window =>
            string.Equals(
                WindowTargetKeyFactory.Create(window),
                normalizedTargetId,
                StringComparison.Ordinal
            )
        );
        if (matchingWindow is null)
            return false;

        _accessor.RaiseWindow(matchingWindow.WindowId);
        lock (_syncRoot)
        {
            _lastActivatedClientId = matchingWindow.WindowId;
        }

        WindowActivated?.Invoke(this, matchingWindow.WindowId);
        return true;
    }

    private bool TryActivateRelativeClient(int step)
    {
        if (step is not (1 or -1))
            throw new ArgumentOutOfRangeException(nameof(step));

        IReadOnlyCollection<WindowConfig> windows = _accessor.GetWindows();
        IReadOnlyList<WindowConfig> candidates = _cycleCandidatesResolver(windows);
        if (candidates.Count == 0)
            return false;

        WindowConfig target;
        lock (_syncRoot)
        {
            int currentIndex = FindIndexByWindowId(candidates, _lastActivatedClientId);

            int nextIndex;
            if (currentIndex < 0)
                nextIndex = step > 0 ? 0 : candidates.Count - 1;
            else
                nextIndex =
                    step > 0
                        ? (currentIndex + 1) % candidates.Count
                        : (currentIndex - 1 + candidates.Count) % candidates.Count;

            target = candidates[nextIndex];
            _lastActivatedClientId = target.WindowId;
        }

        _accessor.RaiseWindow(target.WindowId);
        WindowActivated?.Invoke(this, target.WindowId);
        return true;
    }

    private static int FindIndexByWindowId(IReadOnlyList<WindowConfig> windows, string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return -1;

        for (int i = 0; i < windows.Count; i++)
        {
            if (string.Equals(windows[i].WindowId, windowId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static IReadOnlyList<WindowConfig> ResolveSelectedCycleCandidates(
        IReadOnlyCollection<WindowConfig> windows
    )
    {
        ArgumentNullException.ThrowIfNull(windows);

        SelectionSnapshot snapshot = ConfigFileAccessor.GetInstance().ReadConfig(config =>
            new SelectionSnapshot(
                config.BlacklistPrefixes.ToHashSet(StringComparer.OrdinalIgnoreCase),
                config.WhitelistPrefixes
                    .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                    .ToArray()
            )
        );

        var selected = new List<WindowConfig>(capacity: windows.Count);
        foreach (WindowConfig window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.WindowId))
                continue;

            if (snapshot.Blacklist.Contains(window.WindowTitle))
                continue;

            bool matchesWhitelist = snapshot.Whitelist.Any(prefix =>
                window.WindowTitle.Contains(prefix, StringComparison.OrdinalIgnoreCase)
            );
            if (!matchesWhitelist)
                continue;

            selected.Add(window);
        }

        return selected;
    }

    private sealed record SelectionSnapshot(
        HashSet<string> Blacklist,
        string[] Whitelist
    );
}
