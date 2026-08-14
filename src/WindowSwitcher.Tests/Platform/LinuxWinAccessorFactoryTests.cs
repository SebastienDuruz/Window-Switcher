using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxWinAccessorFactoryTests
{
    [Fact]
    public void Create_ReturnsSingleEwmhAccessor()
    {
        var factory = new LinuxWinAccessorFactory();

        WinAccessorBase accessor = factory.Create();

        Assert.IsType<X11EwmhWindowAccessor>(accessor);
        Assert.IsAssignableFrom<IDisposable>(accessor).Dispose();
    }
}
