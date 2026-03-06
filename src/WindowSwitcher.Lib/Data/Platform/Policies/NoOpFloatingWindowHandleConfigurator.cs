using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.Policies;

/// <summary>
/// No-op handle configurator for platforms without extra native behavior.
/// </summary>
public sealed class NoOpFloatingWindowHandleConfigurator : IFloatingWindowHandleConfigurator
{
    public void Configure(nint windowHandle) { }
}
