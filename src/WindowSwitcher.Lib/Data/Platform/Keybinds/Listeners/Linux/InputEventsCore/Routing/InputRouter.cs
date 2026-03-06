using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Options;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Routing;

/// <summary>
/// Applies include/exclude filters to discovered devices and decoded events.
/// </summary>
internal sealed class InputRouter
{
    private readonly HashSet<string>? _includeDevicePaths;
    private readonly HashSet<string>? _excludeDevicePaths;
    private readonly HashSet<DeviceKind>? _includeKinds;
    private readonly HashSet<ushort>? _includeEventTypes;
    private readonly HashSet<KeyCode>? _includeKeys;

    /// <summary>
    /// Builds a router from service options.
    /// </summary>
    /// <param name="options">Filter configuration for devices and event payloads.</param>
    public InputRouter(InputEventServiceOptions options)
    {
        _includeDevicePaths = ToSet(options.IncludeDevicePaths);
        _excludeDevicePaths = ToSet(options.ExcludeDevicePaths);
        _includeKinds = options.IncludeKinds.Length == 0 ? null : [.. options.IncludeKinds];
        _includeEventTypes = options.IncludeEventTypes is null || options.IncludeEventTypes.Length == 0
            ? null
            : [.. options.IncludeEventTypes];
        _includeKeys = options.IncludeKeys is null || options.IncludeKeys.Length == 0
            ? null
            : [.. options.IncludeKeys];
    }

    /// <summary>
    /// Determines whether a device should have a reader attached.
    /// </summary>
    /// <param name="device">Candidate device metadata.</param>
    /// <returns><see langword="true"/> when the device matches all configured filters.</returns>
    public bool ShouldTrackDevice(InputDeviceInfo device)
    {
        if (!device.IsAccessible)
        {
            return false;
        }

        if (_includeDevicePaths is not null && !_includeDevicePaths.Contains(device.Path))
        {
            return false;
        }

        if (_excludeDevicePaths is not null && _excludeDevicePaths.Contains(device.Path))
        {
            return false;
        }

        if (_includeKinds is not null && !_includeKinds.Contains(device.Kind))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a decoded event should be published.
    /// </summary>
    /// <param name="inputEvent">Decoded event candidate.</param>
    /// <returns><see langword="true"/> when the event matches all configured event filters.</returns>
    public bool ShouldEmit(InputEvent inputEvent)
    {
        if (_includeEventTypes is not null && !_includeEventTypes.Contains(inputEvent.Type))
        {
            return false;
        }

        if (inputEvent is KeyEvent keyEvent && _includeKeys is not null && !_includeKeys.Contains(keyEvent.Key))
        {
            return false;
        }

        return true;
    }

    private static HashSet<string>? ToSet(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        // Normalize include/exclude path lists to non-empty, exact-match sets.
        var set = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        return set.Count == 0 ? null : set;
    }
}
