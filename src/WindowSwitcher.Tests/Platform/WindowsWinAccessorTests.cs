using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class WindowsWinAccessorTests
{
    [Fact]
    public async Task TakeScreenshot_ReturnsNull_OnWindowsAccessor()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sut = new WindowsWinAccessor();

        Assert.Null(sut.TakeScreenshot("123"));
        Assert.Null(sut.TakeScreenshot("123", new ScreenshotRequest()));
        Assert.Null(await sut.TakeScreenshotAsync("123", new ScreenshotRequest()));
    }
}
