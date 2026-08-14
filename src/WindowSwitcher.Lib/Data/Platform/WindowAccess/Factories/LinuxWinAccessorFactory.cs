using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Linux accessor factory selecting an accessor from the active desktop session.
/// </summary>
public sealed class LinuxWinAccessorFactory : IWinAccessorFactory
{
    private readonly Func<string?> _sessionTypeResolver;

    /// <summary>
    /// Creates a Linux accessor factory.
    /// </summary>
    public LinuxWinAccessorFactory()
        : this(LinuxSessionDetector.GetSessionType) { }

    internal LinuxWinAccessorFactory(Func<string?> sessionTypeResolver)
    {
        ArgumentNullException.ThrowIfNull(sessionTypeResolver);

        _sessionTypeResolver = sessionTypeResolver;
    }

    /// <inheritdoc />
    public WinAccessorBase Create()
    {
        string sessionType = NormalizeSessionType(_sessionTypeResolver());
        if (string.Equals(sessionType, "wayland", StringComparison.Ordinal))
            return new WaylandWinAccessor();

        return new X11WinAccessor();
    }

    private static string NormalizeSessionType(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
