using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Linux accessor factory.
/// </summary>
public sealed class LinuxWinAccessorFactory : IWinAccessorFactory
{
    /// <inheritdoc />
    public WinAccessorBase Create()
    {
        return new LinuxWinAccessor();
    }
}
