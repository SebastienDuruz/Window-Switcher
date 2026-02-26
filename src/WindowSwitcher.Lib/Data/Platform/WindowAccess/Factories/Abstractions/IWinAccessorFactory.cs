using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;

/// <summary>
/// Creates window accessor implementations for the current runtime environment.
/// </summary>
public interface IWinAccessorFactory
{
    /// <summary>
    /// Creates a window accessor instance.
    /// </summary>
    WinAccessorBase Create();
}
