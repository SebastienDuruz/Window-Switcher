using System.Threading.Channels;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Options;

/// <summary>
/// Configuration for discovery, filtering, and buffering behavior of <c>InputEventService</c>.
/// </summary>
public sealed class InputEventServiceOptions
{
    /// <summary>
    /// Explicit device paths to include (for example <c>/dev/input/event3</c>). <see langword="null"/> means no explicit include list.
    /// </summary>
    public string[]? IncludeDevicePaths { get; init; }

    /// <summary>
    /// Device paths to exclude after inclusion logic has been applied.
    /// </summary>
    public string[]? ExcludeDevicePaths { get; init; }

    /// <summary>
    /// Device kinds to track. Defaults to keyboard-only to reduce accidental over-collection.
    /// </summary>
    public DeviceKind[] IncludeKinds { get; init; } = [DeviceKind.Keyboard];

    /// <summary>
    /// When <see langword="true"/>, periodically scans <c>/dev/input</c> and starts readers for newly discovered devices.
    /// </summary>
    public bool AutoDiscover { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, a reader attempts to reopen a device after disconnect/read failures.
    /// </summary>
    public bool ReconnectOnDisconnect { get; init; } = true;

    /// <summary>
    /// Delay between reconnect attempts. Non-positive values are normalized internally.
    /// </summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Number of native <c>input_event</c> entries requested per read call.
    /// </summary>
    public int ReadBufferEvents { get; init; } = 64;

    /// <summary>
    /// Maximum number of decoded events buffered in the shared output channel.
    /// </summary>
    public int ChannelCapacity { get; init; } = 1_024;

    /// <summary>
    /// Backpressure strategy used when the output channel reaches capacity.
    /// </summary>
    public BoundedChannelFullMode ChannelFullMode { get; init; } =
        BoundedChannelFullMode.DropOldest;

    /// <summary>
    /// Period between auto-discovery scans when <see cref="AutoDiscover"/> is enabled.
    /// </summary>
    public TimeSpan DiscoveryInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Optional allow-list of raw Linux event types (<c>EV_*</c>).
    /// </summary>
    public ushort[]? IncludeEventTypes { get; init; }

    /// <summary>
    /// Optional allow-list of key codes applied only to <see cref="KeyEvent"/> events.
    /// </summary>
    public KeyCode[]? IncludeKeys { get; init; }
}
