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
            Assert.IsType<X11EwmhWindowAccessor>(accessor);
        else if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsWinAccessor>(accessor);
    }
}
