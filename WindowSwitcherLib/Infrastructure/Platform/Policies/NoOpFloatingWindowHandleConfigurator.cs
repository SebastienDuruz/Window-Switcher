using WindowSwitcherLib.Application.Platform;

namespace WindowSwitcherLib.Infrastructure.Platform.Policies;

/// <summary>
/// No-op handle configurator for platforms without extra native behavior.
/// </summary>
public sealed class NoOpFloatingWindowHandleConfigurator : IFloatingWindowHandleConfigurator
{
    public void Configure(nint windowHandle)
    {
    }
}
