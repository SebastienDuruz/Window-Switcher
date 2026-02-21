using System.Runtime.Versioning;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Windows accessor factory.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWinAccessorFactory : IWinAccessorFactory
{
    /// <inheritdoc />
    public WinAccessorBase Create()
    {
        return new WindowsWinAccessor();
    }
}
