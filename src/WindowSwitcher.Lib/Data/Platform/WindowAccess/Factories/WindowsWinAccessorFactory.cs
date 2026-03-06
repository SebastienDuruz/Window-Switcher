using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

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
