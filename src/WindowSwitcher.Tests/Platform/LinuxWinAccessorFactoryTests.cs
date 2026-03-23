using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxWinAccessorFactoryTests
{
    [Fact]
    public void Create_ReturnsWaylandAccessor_WhenSessionIsWayland()
    {
        var factory = new LinuxWinAccessorFactory(() => "wayland");

        WinAccessorBase accessor = factory.Create();

        Assert.IsType<WaylandWinAccessor>(accessor);
    }

    [Fact]
    public void Create_ReturnsX11Accessor_WhenSessionIsX11()
    {
        var factory = new LinuxWinAccessorFactory(() => "x11");

        WinAccessorBase accessor = factory.Create();

        Assert.IsType<X11WinAccessor>(accessor);
    }

    [Fact]
    public void Create_ReturnsX11Accessor_WhenSessionIsUnknown()
    {
        var factory = new LinuxWinAccessorFactory(() => null);

        WinAccessorBase accessor = factory.Create();

        Assert.IsType<X11WinAccessor>(accessor);
    }
}
