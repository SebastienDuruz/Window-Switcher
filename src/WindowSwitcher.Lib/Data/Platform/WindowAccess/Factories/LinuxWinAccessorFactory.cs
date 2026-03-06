using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

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
