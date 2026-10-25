using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class RuntimeWinAccessorFactoryTests
{
    [Fact]
    public void Create_ReturnsAccessorForCurrentOperatingSystem()
    {
        var factory = new RuntimeWinAccessorFactory();

        using WinAccessorBase accessor = factory.Create();

        if (OperatingSystem.IsLinux())
            Assert.True(accessor is X11EwmhWindowAccessor or CompositeWinAccessor);
        else if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsWinAccessor>(accessor);
    }

    [Fact]
    public void CreateLinux_CombinesEwmhAndKWinOnKdeWayland()
    {
        using WinAccessorBase accessor = RuntimeWinAccessorFactory.CreateLinux(
            LinuxSessionKind.Wayland,
            LinuxDesktopKind.Kde
        );

        Assert.IsType<CompositeWinAccessor>(accessor);
    }

    [Theory]
    [InlineData(LinuxSessionKind.Wayland, LinuxDesktopKind.Other)]
    [InlineData(LinuxSessionKind.X11, LinuxDesktopKind.Kde)]
    [InlineData(LinuxSessionKind.Unsupported, LinuxDesktopKind.Kde)]
    public void CreateLinux_UsesEwmhOutsideKdeWayland(
        LinuxSessionKind session,
        LinuxDesktopKind desktop
    )
    {
        using WinAccessorBase accessor = RuntimeWinAccessorFactory.CreateLinux(session, desktop);

        Assert.IsType<X11EwmhWindowAccessor>(accessor);
    }
}
