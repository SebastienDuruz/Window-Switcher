using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Backward-compatible static entry point for obtaining window accessors.
/// </summary>
public static class AccessorFactory
{
    private static IWinAccessorFactory _current = new RuntimeWinAccessorFactory();

    /// <summary>
    /// Gets or sets the underlying accessor factory implementation.
    /// </summary>
    public static IWinAccessorFactory Current
    {
        get => _current;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _current = value;
        }
    }

    /// <summary>
    /// Creates a window accessor using <see cref="Current"/>.
    /// </summary>
    public static WinAccessorBase GetAccessor()
    {
        return Current.Create();
    }
}
