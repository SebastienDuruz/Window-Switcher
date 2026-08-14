namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

/// <summary>
/// Indicates that Linux global keyboard capture cannot start because input devices are inaccessible.
/// </summary>
public sealed class LinuxInputAccessException : InvalidOperationException
{
    /// <summary>
    /// Creates a new exception for inaccessible Linux input devices.
    /// </summary>
    public LinuxInputAccessException(IReadOnlyCollection<string> devicePaths)
        : base(CreateMessage(devicePaths))
    {
        ArgumentNullException.ThrowIfNull(devicePaths);

        DevicePaths = devicePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Gets the inaccessible device paths observed during startup.
    /// </summary>
    public IReadOnlyList<string> DevicePaths { get; }

    private static string CreateMessage(IReadOnlyCollection<string> devicePaths)
    {
        ArgumentNullException.ThrowIfNull(devicePaths);

        string[] normalizedPaths = devicePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string paths =
            normalizedPaths.Length == 0 ? "/dev/input/event*" : string.Join(", ", normalizedPaths);

        return $"Global keyboard startup failed on Linux due to inaccessible input devices ({paths}). Reading /dev/input/event* requires elevated access. Use root, add the user to the input group, or configure udev rules.";
    }
}
